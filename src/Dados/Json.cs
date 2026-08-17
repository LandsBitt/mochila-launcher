using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace Mochila.Dados
{
    /// <summary>
    /// Objeto JSON com a ordem das chaves preservada.
    /// Dictionary não garante ordem, e o arquivo precisa sair legível e estável
    /// (edito na mão de vez em quando, e diff bagunçado não ajuda ninguém).
    /// </summary>
    public sealed class JsonObjeto
    {
        private readonly List<KeyValuePair<string, object?>> _itens = new List<KeyValuePair<string, object?>>();

        public int Quantidade => _itens.Count;

        public IReadOnlyList<KeyValuePair<string, object?>> Itens => _itens;

        /// <summary>Acrescenta uma chave no fim. Devolve o próprio objeto para encadear.</summary>
        public JsonObjeto Add(string chave, object? valor)
        {
            _itens.Add(new KeyValuePair<string, object?>(chave, valor));
            return this;
        }
    }

    /// <summary>
    /// JSON sem NuGet.
    ///
    /// Leitura: JavaScriptSerializer (System.Web.Extensions), que já resolve parsing e escapes.
    /// Escrita: serializador próprio, curto, para ter indentação e ordem de chaves controladas.
    /// O mapeamento chave &lt;-&gt; propriedade fica explícito nos modelos, então nada quebra
    /// silenciosamente quando um campo for renomeado.
    /// </summary>
    public static class Json
    {
        private const string Recuo = "  ";
        private const string FimDeLinha = "\r\n";

        // ---- Leitura ---------------------------------------------------------------------

        /// <summary>
        /// Analisa um texto JSON. Devolve Dictionary&lt;string, object&gt; para objetos,
        /// object[] para listas, e string/bool/número/null para escalares.
        /// </summary>
        public static object? Analisar(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;

            var serializador = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return serializador.DeserializeObject(texto);
        }

        public static Dictionary<string, object>? ComoObjeto(object? valor) => valor as Dictionary<string, object>;

        public static List<object?> ComoLista(object? valor)
        {
            var resultado = new List<object?>();
            if (valor is string || valor is not IEnumerable enumeravel) return resultado;

            foreach (var item in enumeravel) resultado.Add(item);
            return resultado;
        }

        private static object? Bruto(Dictionary<string, object>? objeto, string chave)
        {
            if (objeto is null || !objeto.TryGetValue(chave, out var valor)) return null;
            return valor;
        }

        public static string? Texto(Dictionary<string, object>? objeto, string chave, string? padrao)
        {
            return Bruto(objeto, chave) switch
            {
                null => padrao,
                string texto => texto,
                var outro => Convert.ToString(outro, CultureInfo.InvariantCulture)
            };
        }

        public static int Inteiro(Dictionary<string, object>? objeto, string chave, int padrao)
            => InteiroOpcional(objeto, chave) ?? padrao;

        public static int? InteiroOpcional(Dictionary<string, object>? objeto, string chave)
        {
            var valor = Bruto(objeto, chave);
            if (valor is null) return null;

            if (valor is string texto)
            {
                return int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var doTexto)
                    ? doTexto
                    : (int?)null;
            }

            try
            {
                return Convert.ToInt32(valor, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Número fracionário. O "score" do SteamGridDB às vezes vem inteiro, às vezes
        /// com casa decimal, e é por ele que as capas são ordenadas.
        /// </summary>
        public static double Numero(Dictionary<string, object>? objeto, string chave, double padrao)
        {
            var valor = Bruto(objeto, chave);
            if (valor is null) return padrao;

            if (valor is string texto)
            {
                return double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var doTexto)
                    ? doTexto
                    : padrao;
            }

            try
            {
                return Convert.ToDouble(valor, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return padrao;
            }
        }

        public static bool Booleano(Dictionary<string, object>? objeto, string chave, bool padrao)
        {
            var valor = Bruto(objeto, chave);
            if (valor is null) return padrao;
            if (valor is bool booleano) return booleano;

            return bool.TryParse(Convert.ToString(valor, CultureInfo.InvariantCulture), out var convertido)
                ? convertido
                : padrao;
        }

        /// <summary>Lê uma data ISO-8601 em UTC. Qualquer coisa inválida vira null.</summary>
        public static DateTime? DataOpcional(Dictionary<string, object>? objeto, string chave)
        {
            if (Bruto(objeto, chave) is not string texto || texto.Length == 0) return null;

            if (DateTime.TryParse(texto, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var data))
            {
                return DateTime.SpecifyKind(data, DateTimeKind.Utc);
            }
            return null;
        }

        /// <summary>Formata uma data como ISO-8601 UTC ("2026-08-11T20:15:00Z"), ou null.</summary>
        public static string? FormatarData(DateTime? data)
        {
            if (data is not { } valor) return null;

            var utc = valor.Kind == DateTimeKind.Utc ? valor : valor.ToUniversalTime();
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converte um valor cru do parser na forma que o escritor entende.
        ///
        /// Existe pelo saco de sobras da fase 12: um campo que este binário não conhece é
        /// lido como <c>Dictionary</c>/<c>object[]</c> e precisa voltar para o disco com a
        /// mesma cara. Sem esta conversão, um objeto aninhado sairia escrito como lista de
        /// <c>KeyValuePair</c> — o dado seria "preservado" e ilegível, que é pior que
        /// perdê-lo com aviso.
        /// </summary>
        public static object? ParaEscrita(object? valor)
        {
            if (valor is null || valor is string || valor is bool || valor is JsonObjeto) return valor;

            if (valor is Dictionary<string, object> objeto)
            {
                var convertido = new JsonObjeto();
                foreach (var par in objeto) convertido.Add(par.Key, ParaEscrita(par.Value));
                return convertido;
            }

            if (valor is IEnumerable lista)
            {
                var itens = new List<object?>();
                foreach (var item in lista) itens.Add(ParaEscrita(item));
                return itens;
            }

            return valor;   // número, data: o escritor já sabe
        }

        // ---- Escrita ---------------------------------------------------------------------

        /// <summary>Serializa em JSON indentado (2 espaços), terminando com quebra de linha.</summary>
        public static string Escrever(object? valor)
        {
            var sb = new StringBuilder(4096);
            EscreverValor(sb, valor, 0);
            sb.Append(FimDeLinha);
            return sb.ToString();
        }

        private static void EscreverValor(StringBuilder sb, object? valor, int nivel)
        {
            switch (valor)
            {
                case null:
                    sb.Append("null");
                    return;

                case string texto:
                    EscreverTexto(sb, texto);
                    return;

                case bool booleano:
                    sb.Append(booleano ? "true" : "false");
                    return;

                case int or long or short or byte or uint or ulong:
                    sb.Append(Convert.ToInt64(valor, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    return;

                case double or float or decimal:
                    sb.Append(Convert.ToDouble(valor, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                    return;

                case DateTime data:
                    EscreverTexto(sb, FormatarData(data)!);
                    return;

                // JsonObjeto antes de IEnumerable: a ordem das chaves importa.
                case JsonObjeto objeto:
                    EscreverObjeto(sb, objeto, nivel);
                    return;

                case IEnumerable lista:
                    EscreverLista(sb, lista, nivel);
                    return;

                default:
                    EscreverTexto(sb, Convert.ToString(valor, CultureInfo.InvariantCulture) ?? "");
                    return;
            }
        }

        private static void EscreverObjeto(StringBuilder sb, JsonObjeto objeto, int nivel)
        {
            if (objeto.Quantidade == 0)
            {
                sb.Append("{}");
                return;
            }

            sb.Append('{').Append(FimDeLinha);
            for (var i = 0; i < objeto.Itens.Count; i++)
            {
                // KeyValuePair não tem Deconstruct no net48.
                var item = objeto.Itens[i];
                Indentar(sb, nivel + 1);
                EscreverTexto(sb, item.Key);
                sb.Append(": ");
                EscreverValor(sb, item.Value, nivel + 1);
                if (i < objeto.Itens.Count - 1) sb.Append(',');
                sb.Append(FimDeLinha);
            }
            Indentar(sb, nivel);
            sb.Append('}');
        }

        private static void EscreverLista(StringBuilder sb, IEnumerable lista, int nivel)
        {
            var itens = new List<object?>();
            foreach (var item in lista) itens.Add(item);

            if (itens.Count == 0)
            {
                sb.Append("[]");
                return;
            }

            sb.Append('[').Append(FimDeLinha);
            for (var i = 0; i < itens.Count; i++)
            {
                Indentar(sb, nivel + 1);
                EscreverValor(sb, itens[i], nivel + 1);
                if (i < itens.Count - 1) sb.Append(',');
                sb.Append(FimDeLinha);
            }
            Indentar(sb, nivel);
            sb.Append(']');
        }

        private static void Indentar(StringBuilder sb, int nivel)
        {
            for (var i = 0; i < nivel; i++) sb.Append(Recuo);
        }

        private static void EscreverTexto(StringBuilder sb, string? texto)
        {
            if (texto is null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');
            foreach (var c in texto)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);   // acentos vão literais; o arquivo é UTF-8
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
