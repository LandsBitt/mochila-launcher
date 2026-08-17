using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mochila.Modelo
{
    /// <summary>
    /// As perguntas que se fazem ao histórico: quanto joguei por mês, em que dias, quais
    /// os dez mais jogados, quanto foi esta semana.
    ///
    /// <b>Tudo aqui é conta em horário LOCAL sobre datas gravadas em UTC.</b> É a única
    /// coisa desta fase que erra fácil e cala: "horas por mês" e "mapa de calor" são
    /// perguntas de calendário, e calendário é do fuso de quem olha. Uma sessão de 31/01
    /// às 22h aqui é 01/02 em UTC — somá-la em fevereiro é dar a resposta errada sem
    /// nenhum sintoma.
    ///
    /// <b>Sessão de jogo que não está mais na biblioteca é ignorada, não apagada.</b> A
    /// filtragem é aqui, na leitura; o arquivo continua com ela. Reaproveitar slug é
    /// intencional no projeto, e um jogo removido e re-adicionado volta com o mesmo id —
    /// e com o histórico dele.
    /// </summary>
    public sealed class EstatisticasDeSessoes
    {
        private readonly List<Sessao> _validas = new List<Sessao>();
        private readonly Dictionary<string, Jogo> _jogosPorId =
            new Dictionary<string, Jogo>(StringComparer.OrdinalIgnoreCase);

        public EstatisticasDeSessoes(HistoricoDeSessoes historico, Biblioteca biblioteca)
        {
            if (historico is null) throw new ArgumentNullException(nameof(historico));
            if (biblioteca is null) throw new ArgumentNullException(nameof(biblioteca));

            foreach (var jogo in biblioteca.Jogos)
            {
                if (jogo.Id.Length > 0) _jogosPorId[jogo.Id] = jogo;
            }

            foreach (var sessao in historico.Sessoes)
            {
                if (_jogosPorId.ContainsKey(sessao.JogoId)) _validas.Add(sessao);
            }

            Orfas = historico.Sessoes.Count - _validas.Count;
        }

        /// <summary>Sessões de jogos que não estão mais na biblioteca. Contadas, não apagadas.</summary>
        public int Orfas { get; }

        /// <summary>Quantas sessões entram nas contas.</summary>
        public int Quantidade => _validas.Count;

        public int SegundosTotais
        {
            get
            {
                var total = 0;
                foreach (var sessao in _validas) total += sessao.Segundos;
                return total;
            }
        }

        /// <summary>Anos (locais) que aparecem no histórico, do mais novo para o mais velho.</summary>
        public List<int> AnosComHistorico()
        {
            var anos = new List<int>();
            foreach (var sessao in _validas)
            {
                var ano = sessao.InicioLocal.Year;
                if (!anos.Contains(ano)) anos.Add(ano);
            }

            anos.Sort();
            anos.Reverse();
            return anos;
        }

        // ---- Por jogo (tela de detalhes) -------------------------------------------------------

        /// <summary>
        /// As últimas sessões de um jogo, da mais recente para a mais antiga.
        /// Ordena por data e não pela ordem do arquivo: um histórico editado à mão, ou uma
        /// sessão gravada com o relógio do PC atrasado, não pode embaralhar a lista.
        /// </summary>
        public List<Sessao> UltimasDoJogo(string? jogoId, int quantas)
        {
            var doJogo = new List<Sessao>();
            if (string.IsNullOrEmpty(jogoId)) return doJogo;

            foreach (var sessao in _validas)
            {
                if (string.Equals(sessao.JogoId, jogoId, StringComparison.OrdinalIgnoreCase))
                    doJogo.Add(sessao);
            }

            doJogo.Sort((a, b) => b.InicioUtc.CompareTo(a.InicioUtc));
            if (quantas > 0 && doJogo.Count > quantas) doJogo.RemoveRange(quantas, doJogo.Count - quantas);

            return doJogo;
        }

        /// <summary>Segundos jogados de um jogo no mês (local) da data de referência.</summary>
        public int SegundosDoJogoNoMes(string? jogoId, DateTime referenciaLocal)
        {
            if (string.IsNullOrEmpty(jogoId)) return 0;

            var total = 0;
            foreach (var sessao in _validas)
            {
                if (!string.Equals(sessao.JogoId, jogoId, StringComparison.OrdinalIgnoreCase)) continue;

                var local = sessao.InicioLocal;
                if (local.Year == referenciaLocal.Year && local.Month == referenciaLocal.Month)
                    total += sessao.Segundos;
            }
            return total;
        }

        public int SessoesDoJogo(string? jogoId)
        {
            if (string.IsNullOrEmpty(jogoId)) return 0;

            var quantas = 0;
            foreach (var sessao in _validas)
            {
                if (string.Equals(sessao.JogoId, jogoId, StringComparison.OrdinalIgnoreCase)) quantas++;
            }
            return quantas;
        }

        // ---- Do acervo (tela de estatísticas) --------------------------------------------------

        /// <summary>Doze posições, de janeiro a dezembro, em segundos. Índice 0 = janeiro.</summary>
        public int[] SegundosPorMes(int ano)
        {
            var meses = new int[12];
            foreach (var sessao in _validas)
            {
                var local = sessao.InicioLocal;
                if (local.Year == ano) meses[local.Month - 1] += sessao.Segundos;
            }
            return meses;
        }

        /// <summary>
        /// Segundos por dia do ano, para o mapa de calor. A chave é a data local sem hora.
        /// Dia sem jogo simplesmente não aparece — o mapa desenha o vazio a partir disso.
        /// </summary>
        public Dictionary<DateTime, int> SegundosPorDia(int ano)
        {
            var dias = new Dictionary<DateTime, int>();
            foreach (var sessao in _validas)
            {
                var local = sessao.InicioLocal;
                if (local.Year != ano) continue;

                var dia = local.Date;
                dias.TryGetValue(dia, out var acumulado);
                dias[dia] = acumulado + sessao.Segundos;
            }
            return dias;
        }

        /// <summary>
        /// Os jogos com mais tempo no histórico, do maior para o menor.
        ///
        /// Sai do histórico, não de <c>segundosJogados</c>: aquele é o acumulado de sempre
        /// (inclui biblioteca migrada sem histórico) e este responde "o que eu joguei desde
        /// que existe registro". São números diferentes de propósito, e a tela diz qual é qual.
        /// </summary>
        public List<TotalDoJogo> Top(int quantos, int? ano = null)
        {
            var porJogo = new Dictionary<string, TotalDoJogo>(StringComparer.OrdinalIgnoreCase);

            foreach (var sessao in _validas)
            {
                if (ano is { } filtro && sessao.InicioLocal.Year != filtro) continue;

                if (!porJogo.TryGetValue(sessao.JogoId, out var total))
                {
                    total = new TotalDoJogo(_jogosPorId[sessao.JogoId]);
                    porJogo[sessao.JogoId] = total;
                }

                total.Somar(sessao.Segundos);
            }

            var lista = new List<TotalDoJogo>(porJogo.Values);
            lista.Sort((a, b) =>
            {
                var porTempo = b.Segundos.CompareTo(a.Segundos);
                return porTempo != 0
                    ? porTempo
                    : string.Compare(a.Jogo.Titulo, b.Jogo.Titulo, StringComparison.CurrentCultureIgnoreCase);
            });

            if (quantos > 0 && lista.Count > quantos) lista.RemoveRange(quantos, lista.Count - quantos);
            return lista;
        }

        /// <summary>
        /// Segundos desde o começo da semana local. O primeiro dia da semana vem da cultura
        /// do sistema (domingo no pt-BR) em vez de ser chutado: "esta semana" é o que o
        /// calendário da máquina chama de semana.
        /// </summary>
        public int SegundosNaSemana(DateTime referenciaLocal)
        {
            var inicio = InicioDaSemana(referenciaLocal);
            var fim = inicio.AddDays(7);

            var total = 0;
            foreach (var sessao in _validas)
            {
                var local = sessao.InicioLocal;
                if (local >= inicio && local < fim) total += sessao.Segundos;
            }
            return total;
        }

        public static DateTime InicioDaSemana(DateTime referenciaLocal)
        {
            var primeiro = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            var hoje = (int)referenciaLocal.DayOfWeek;
            var recuo = (hoje - primeiro + 7) % 7;

            return referenciaLocal.Date.AddDays(-recuo);
        }

        /// <summary>Um jogo e quanto tempo ele soma no histórico. Usado pelo top 10.</summary>
        public sealed class TotalDoJogo
        {
            public TotalDoJogo(Jogo jogo)
            {
                Jogo = jogo;
            }

            public Jogo Jogo { get; }

            public int Segundos { get; private set; }

            public int Sessoes { get; private set; }

            internal void Somar(int segundos)
            {
                Segundos += segundos;
                Sessoes++;
            }
        }
    }
}
