using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// Tag, nota, status, favorito e remoção — aplicados a um jogo ou a trinta pelo mesmo
    /// caminho.
    ///
    /// A fase 12 traz isto junto com os campos, e não depois, por um motivo prático: sem
    /// ação em lote, etiquetar duzentos jogos é duzentas idas ao menu de contexto, e a
    /// feature nasce morta. Quem decide os alvos é a grade
    /// (<see cref="GradeDeCapas.SelecaoParaAcao"/>), que só devolve jogos visíveis — é
    /// assim que "ação em lote não toca em jogo fora do filtro" fica garantido num lugar
    /// só, em vez de em cada ação.
    /// </summary>
    public sealed class AcoesEmLote
    {
        private readonly IContextoDoLauncher _contexto;
        private readonly AcoesDoJogo _acoes;

        public AcoesEmLote(IContextoDoLauncher contexto, AcoesDoJogo acoes)
        {
            _contexto = contexto ?? throw new ArgumentNullException(nameof(contexto));
            _acoes = acoes ?? throw new ArgumentNullException(nameof(acoes));
        }

        private Form Janela => _contexto.Janela;

        // ---- Tags ------------------------------------------------------------------------------

        public void AplicarTag(IList<Jogo> alvos)
        {
            if (!TemAlvo(alvos)) return;

            if (PerguntarTag("Aplicar tag", $"Que tag entra em {Descrever(alvos)}?") is not { } tag) return;

            var mudou = 0;
            foreach (var jogo in alvos)
            {
                if (Etiquetas.Acrescentar(jogo.Tags, tag)) mudou++;
            }

            Concluir($"#{tag} aplicada a {mudou} jogo(s).", mudou);
        }

        public void RemoverTag(IList<Jogo> alvos)
        {
            if (!TemAlvo(alvos)) return;

            if (PerguntarTag("Remover tag", $"Que tag sai de {Descrever(alvos)}?") is { } tag)
                RemoverTag(alvos, tag);
        }

        /// <summary>
        /// Tira uma tag já conhecida, sem perguntar. É o caminho de clicar na etiqueta
        /// dentro da tela de detalhes, onde eu já apontei para qual delas quero tirar.
        /// </summary>
        public void RemoverTag(IList<Jogo> alvos, string tag)
        {
            if (!TemAlvo(alvos)) return;

            var mudou = 0;
            foreach (var jogo in alvos)
            {
                if (Etiquetas.Remover(jogo.Tags, tag)) mudou++;
            }

            Concluir($"#{tag} removida de {mudou} jogo(s).", mudou);
        }

        /// <summary>
        /// Só as tags do acervo inteiro vão para a lista do diálogo — inclusive as que o
        /// filtro atual esconde, que são justamente as que eu quero reaproveitar em vez de
        /// redigitar torto.
        /// </summary>
        private string? PerguntarTag(string titulo, string explicacao)
        {
            using (var janela = new FormTag(titulo, explicacao, Etiquetas.PorFrequencia(_contexto.Biblioteca.Jogos)))
            {
                return janela.ShowDialog(Janela) == DialogResult.OK ? janela.TagEscolhida : null;
            }
        }

        // ---- Nota, status e favorito ------------------------------------------------------------

        public void DefinirStatus(IList<Jogo> alvos, StatusDoJogo status)
        {
            if (!TemAlvo(alvos)) return;

            foreach (var jogo in alvos) jogo.Status = status;

            Concluir($"{Descrever(alvos)}: {Estados.Descrever(status).ToLowerInvariant()}.", alvos.Count);
        }

        public void DefinirNota(IList<Jogo> alvos, int nota)
        {
            if (!TemAlvo(alvos)) return;

            var valor = Math.Max(0, Math.Min(5, nota));
            foreach (var jogo in alvos) jogo.Nota = valor;

            Concluir(valor == 0
                ? $"Nota apagada de {Descrever(alvos)}."
                : $"{Descrever(alvos)}: nota {valor}.", alvos.Count);
        }

        /// <summary>
        /// Favoritar em lote liga todos, a menos que já estejam todos ligados — aí desliga.
        /// Alternar um a um deixaria a seleção metade sim, metade não, que não é o que
        /// alguém quer de uma ação em lote.
        /// </summary>
        public void AlternarFavorito(IList<Jogo> alvos)
        {
            if (!TemAlvo(alvos)) return;

            var todosFavoritos = true;
            foreach (var jogo in alvos)
            {
                if (!jogo.Favorito) { todosFavoritos = false; break; }
            }

            foreach (var jogo in alvos) jogo.Favorito = !todosFavoritos;

            Concluir(todosFavoritos
                ? $"{Descrever(alvos)} não é mais favorito."
                : $"{Descrever(alvos)} agora é favorito.", alvos.Count);
        }

        // ---- Remoção -----------------------------------------------------------------------------

        /// <summary>
        /// Tira vários da biblioteca. A contagem vai no texto da confirmação, e a promessa
        /// de sempre continua escrita com todas as letras: nenhum arquivo do jogo é tocado.
        /// </summary>
        public void Remover(IList<Jogo> alvos)
        {
            if (!TemAlvo(alvos)) return;

            if (alvos.Count == 1)
            {
                // Um só continua indo pelo caminho de sempre, que já explica tudo.
                _acoes.RemoverDaBiblioteca(alvos[0]);
                return;
            }

            var resposta = MessageBox.Show(Janela,
                $"Tirar {alvos.Count} jogos da biblioteca?{Environment.NewLine}{Environment.NewLine}" +
                $"{Listar(alvos)}{Environment.NewLine}{Environment.NewLine}" +
                "Somem os cards, as capas e o tempo jogado de todos eles. " +
                "OS JOGOS EM DISCO NÃO SÃO TOCADOS — nenhum arquivo é apagado, e um novo " +
                "scan (F6) encontra todos de volta.",
                "Remover da biblioteca", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (resposta != DialogResult.Yes) return;

            foreach (var jogo in alvos)
            {
                _acoes.EsquecerArte(jogo);
                _contexto.Biblioteca.Jogos.Remove(jogo);
            }

            _contexto.SalvarBiblioteca();
            _contexto.ReaplicarFiltros();
            _contexto.Avisar($"{alvos.Count} jogos saíram da biblioteca. Os arquivos continuam no HD.");
        }

        // ---- Apoio -------------------------------------------------------------------------------

        private bool TemAlvo(IList<Jogo> alvos)
        {
            if (alvos != null && alvos.Count > 0) return true;

            _contexto.Avisar("Nenhum jogo selecionado.");
            return false;
        }

        /// <summary>"\"NFS Carbon\"" para um; "12 jogos" para vários.</summary>
        private static string Descrever(IList<Jogo> alvos)
            => alvos.Count == 1 ? $"\"{alvos[0].Titulo}\"" : $"{alvos.Count} jogos";

        /// <summary>Os primeiros títulos, para a confirmação não ser uma contagem cega.</summary>
        private static string Listar(IList<Jogo> alvos)
        {
            const int Mostrar = 8;

            var nomes = new List<string>(Math.Min(Mostrar, alvos.Count));
            for (var i = 0; i < alvos.Count && i < Mostrar; i++) nomes.Add(alvos[i].Titulo);

            var texto = string.Join(Environment.NewLine, nomes.ToArray());
            return alvos.Count > Mostrar ? $"{texto}{Environment.NewLine}... e mais {alvos.Count - Mostrar}." : texto;
        }

        private void Concluir(string aviso, int mudancas)
        {
            if (mudancas > 0)
            {
                _contexto.SalvarBiblioteca();

                // Reaplica em vez de só redesenhar: mudar tag, nota ou status pode tirar o
                // jogo do filtro que está ativo agora (@zerado, *4), e a grade tem que
                // refletir isso na hora.
                _contexto.ReaplicarFiltros();
            }

            _contexto.Avisar(aviso);
        }
    }
}
