using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;
using Mochila.Modelo;
using Mochila.Util;

namespace Mochila.UI
{
    /// <summary>
    /// A tela de estatísticas da fase 13: horas por mês, mapa de calor do ano, top 10 e
    /// "jogado esta semana".
    ///
    /// <b>Tudo desenhado em GDI+, nada de biblioteca de gráfico.</b> São três barras e uma
    /// grade de quadradinhos; qualquer dependência aqui custaria mais que o desenho — e a
    /// regra de zero NuGet não abre exceção para enfeite.
    ///
    /// A janela é um <c>Form</c>, e isso não briga com a regra de memória da fase 11: o que
    /// não podia ser um Form era a tela de detalhes, porque ela carrega uma imagem grande
    /// por jogo. Aqui não existe imagem nenhuma — só texto e retângulo.
    /// </summary>
    public sealed class FormEstatisticas : Form
    {
        private const int Margem = 24;

        private readonly EstatisticasDeSessoes _estatisticas;
        private readonly bool _historicoCorrompido;
        private readonly PainelDeEstatisticas _painel;

        private readonly Button _anoAnterior;
        private readonly Button _anoSeguinte;
        private readonly Label _rotuloDoAno;

        private readonly List<int> _anos;
        private int _ano;

        public FormEstatisticas(HistoricoDeSessoes historico, Biblioteca biblioteca)
        {
            if (historico is null) throw new ArgumentNullException(nameof(historico));

            _estatisticas = new EstatisticasDeSessoes(historico, biblioteca);
            _historicoCorrompido = historico.Corrompido;

            Text = "Estatísticas do acervo";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 660);
            MinimumSize = new Size(720, 560);
            BackColor = Tema.Fundo;
            ForeColor = Tema.Texto;
            KeyPreview = true;
            ShowIcon = false;
            MinimizeBox = false;

            Tema.AplicarNaJanela(this);

            _anos = _estatisticas.AnosComHistorico();
            _ano = _anos.Count > 0 ? _anos[0] : DateTime.Now.Year;

            _rotuloDoAno = new Label
            {
                AutoSize = false,
                Size = new Size(80, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Tema.TextoForte,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
            };

            _anoAnterior = Botoes.Criar("‹", Point.Empty, 36, 28);
            _anoAnterior.Click += (_, _) => TrocarAno(-1);

            _anoSeguinte = Botoes.Criar("›", Point.Empty, 36, 28);
            _anoSeguinte.Click += (_, _) => TrocarAno(+1);

            _painel = new PainelDeEstatisticas(this) { Dock = DockStyle.Fill };

            Controls.Add(_painel);
            Controls.Add(MontarBarra());
            Controls.Add(MontarRodape());

            AtualizarAno();

            KeyDown += (_, e) =>
            {
                switch (e.KeyCode)
                {
                    case Keys.Escape: Close(); break;
                    case Keys.Left: TrocarAno(-1); break;
                    case Keys.Right: TrocarAno(+1); break;
                }
            };
        }

        /// <summary>O ano mostrado agora. Só o painel de desenho consulta.</summary>
        private int Ano => _ano;

        private Control MontarBarra()
        {
            var barra = new PainelDeControles
            {
                Dock = DockStyle.Top,
                Height = 52,
                LinhaEmBaixo = true
            };

            var titulo = new Label
            {
                Text = "Histórico de sessões",
                AutoSize = false,
                Location = new Point(Margem, 14),
                Size = new Size(260, 24),
                ForeColor = Tema.TextoForte,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                BackColor = Color.Transparent
            };

            barra.Controls.Add(titulo);
            barra.Controls.Add(_anoAnterior);
            barra.Controls.Add(_rotuloDoAno);
            barra.Controls.Add(_anoSeguinte);

            void Alinhar()
            {
                _anoSeguinte.Left = barra.ClientSize.Width - Margem - _anoSeguinte.Width;
                _anoSeguinte.Top = 12;
                _rotuloDoAno.Left = _anoSeguinte.Left - _rotuloDoAno.Width;
                _rotuloDoAno.Top = 12;
                _anoAnterior.Left = _rotuloDoAno.Left - _anoAnterior.Width;
                _anoAnterior.Top = 12;
            }

            barra.Resize += (_, _) => Alinhar();
            Alinhar();

            return barra;
        }

        private Control MontarRodape()
        {
            var painel = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(Margem, 10, Margem, 10) };

            var fechar = Botoes.Criar("Fechar (Esc)", Point.Empty, 130);
            fechar.Dock = DockStyle.Right;
            fechar.Click += (_, _) => Close();

            painel.Controls.Add(fechar);

            CancelButton = fechar;
            return painel;
        }

        private void TrocarAno(int passo)
        {
            var novo = _ano + passo;

            // Sem histórico em ano nenhum, os botões ficam desligados e isto não roda.
            if (_anos.Count == 0) return;
            if (novo < MenorAno() || novo > MaiorAno()) return;

            _ano = novo;
            AtualizarAno();
        }

        private int MenorAno() => _anos.Count == 0 ? _ano : _anos[_anos.Count - 1];

        private int MaiorAno() => _anos.Count == 0 ? _ano : _anos[0];

        private void AtualizarAno()
        {
            _rotuloDoAno.Text = _ano.ToString(CultureInfo.InvariantCulture);
            _anoAnterior.Enabled = _anos.Count > 0 && _ano > MenorAno();
            _anoSeguinte.Enabled = _anos.Count > 0 && _ano < MaiorAno();

            _painel.Invalidate();
        }

        // ---- O desenho ---------------------------------------------------------------------

        /// <summary>
        /// O corpo da tela. Separado num painel próprio para o desenho ficar longe do
        /// código de janela — e para o clip da rolagem não brigar com os controles da barra.
        /// </summary>
        private sealed class PainelDeEstatisticas : Panel
        {
            private static readonly string[] NomesDosMeses =
                { "jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez" };

            private readonly FormEstatisticas _dono;

            public PainelDeEstatisticas(FormEstatisticas dono)
            {
                _dono = dono;

                SetStyle(ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint |
                         ControlStyles.ResizeRedraw, true);

                DoubleBuffered = true;
                BackColor = Tema.Fundo;
                AutoScroll = true;
            }

            protected override void OnScroll(ScrollEventArgs se)
            {
                base.OnScroll(se);
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Tema.Fundo);
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var estatisticas = _dono._estatisticas;

                // A largura reserva a barra de rolagem esteja ela visível ou não, pela mesma
                // razão da grade (LayoutDaGrade.LarguraUtil): a altura do conteúdo sai deste
                // desenho e vira AutoScrollMinSize, então uma largura que depende da barra
                // fecharia o ciclo "aparece a barra -> muda a largura -> muda a altura ->
                // some a barra" e a janela piscaria sem parar em certas larguras.
                var largura = Math.Max(360, Width - LayoutDaGrade.LarguraDaBarraDeRolagem - (2 * Margem));
                var y = Margem + AutoScrollPosition.Y;

                using (var rotulo = new Font("Segoe UI", 8.5f))
                using (var valor = new Font("Segoe UI", 9.75f))
                using (var forte = new Font("Segoe UI", 9.75f, FontStyle.Bold))
                using (var numeroGrande = new Font("Segoe UI", 18f, FontStyle.Bold))
                {
                    if (estatisticas.Quantidade == 0)
                    {
                        DesenharVazio(g, valor, largura, y);
                        AutoScrollMinSize = new Size(0, 200);
                        return;
                    }

                    y = DesenharResumo(g, rotulo, numeroGrande, largura, y);
                    y = DesenharMeses(g, rotulo, forte, largura, y);
                    y = DesenharMapaDeCalor(g, rotulo, largura, y);
                    y = DesenharTop(g, rotulo, valor, forte, largura, y);
                }

                // A altura do conteúdo sai do desenho, não de uma conta separada: assim não
                // existe layout calculado em dois lugares para sair de sincronia.
                AutoScrollMinSize = new Size(0, y - AutoScrollPosition.Y + Margem);
            }

            private void DesenharVazio(Graphics g, Font fonte, int largura, int y)
            {
                var texto = _dono._historicoCorrompido
                    ? "Não consegui ler o histórico de sessões.\r\n" +
                      "O arquivo com problema é guardado com nome datado na próxima gravação, e a\r\n" +
                      "contagem começa de novo — o tempo total de cada jogo continua na biblioteca."
                    : "Nenhuma sessão registrada ainda.\r\n" +
                      "O histórico começa na primeira vez que você jogar por mais de 5 segundos.";

                TextRenderer.DrawText(g, texto, fonte, new Rectangle(Margem, y + 20, largura, 90),
                    Tema.TextoFraco, TextFormatFlags.Left | TextFormatFlags.WordBreak);
            }

            /// <summary>Os três números de cima: esta semana, este mês e o total do histórico.</summary>
            private int DesenharResumo(Graphics g, Font rotulo, Font numero, int largura, int y)
            {
                var estatisticas = _dono._estatisticas;
                var agora = DateTime.Now;

                // O mês corrente sai da série do ano corrente, que é sempre o de "hoje" —
                // e não do ano escolhido no seletor: "este mês" não muda quando eu folheio
                // 2024 para trás.
                var esteMes = estatisticas.SegundosPorMes(agora.Year)[agora.Month - 1];

                var caixas = new[]
                {
                    new KeyValuePair<string, string>("JOGADO ESTA SEMANA",
                        TempoDeJogo.DescreverCurto(estatisticas.SegundosNaSemana(agora))),
                    new KeyValuePair<string, string>("ESTE MÊS", TempoDeJogo.DescreverCurto(esteMes)),
                    new KeyValuePair<string, string>("TOTAL NO HISTÓRICO",
                        TempoDeJogo.DescreverCurto(estatisticas.SegundosTotais)),
                    new KeyValuePair<string, string>("SESSÕES",
                        estatisticas.Quantidade.ToString(CultureInfo.InvariantCulture))
                };

                var vao = 12;
                var larguraDaCaixa = (largura - (vao * (caixas.Length - 1))) / caixas.Length;
                const int Altura = 78;

                for (var i = 0; i < caixas.Length; i++)
                {
                    var area = new Rectangle(Margem + (i * (larguraDaCaixa + vao)), y, larguraDaCaixa, Altura);

                    using (var caminho = Formas.Arredondado(area, 8))
                    using (var pincel = new SolidBrush(Tema.Superficie))
                    using (var caneta = new Pen(Tema.Borda))
                    {
                        g.FillPath(pincel, caminho);
                        g.DrawPath(caneta, caminho);
                    }

                    TextRenderer.DrawText(g, caixas[i].Key, rotulo,
                        new Rectangle(area.X + 14, area.Y + 12, area.Width - 28, 16), Tema.TextoFraco,
                        TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

                    TextRenderer.DrawText(g, caixas[i].Value, numero,
                        new Rectangle(area.X + 12, area.Y + 30, area.Width - 24, 36), Tema.TextoForte,
                        TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                }

                return y + Altura + 28;
            }

            /// <summary>Horas por mês do ano escolhido, em barras.</summary>
            private int DesenharMeses(Graphics g, Font rotulo, Font forte, int largura, int y)
            {
                var meses = _dono._estatisticas.SegundosPorMes(_dono.Ano);

                Titulo(g, forte, $"Horas por mês em {_dono.Ano.ToString(CultureInfo.InvariantCulture)}",
                       largura, ref y);

                const int AlturaDoGrafico = 150;
                var maior = 0;
                foreach (var segundos in meses) maior = Math.Max(maior, segundos);

                var vao = 10;
                var larguraDaBarra = Math.Max(8, (largura - (vao * 11)) / 12);
                var baseY = y + AlturaDoGrafico;

                using (var caneta = new Pen(Tema.Borda))
                    g.DrawLine(caneta, Margem, baseY, Margem + largura, baseY);

                for (var mes = 0; mes < 12; mes++)
                {
                    var x = Margem + (mes * (larguraDaBarra + vao));

                    // Barra de 3 px quando há tempo mas pouco: um mês com 20 minutos não
                    // pode desaparecer, senão a resposta "joguei nesse mês?" fica errada.
                    var altura = maior == 0 || meses[mes] == 0
                        ? 0
                        : Math.Max(3, (int)Math.Round(meses[mes] / (double)maior * (AlturaDoGrafico - 22)));

                    if (altura > 0)
                    {
                        var barra = new Rectangle(x, baseY - altura, larguraDaBarra, altura);
                        using (var pincel = new SolidBrush(Tema.Acento))
                            g.FillRectangle(pincel, barra);

                        TextRenderer.DrawText(g, TempoDeJogo.DescreverCurto(meses[mes]), rotulo,
                            new Rectangle(x - 4, barra.Y - 17, larguraDaBarra + 8, 16), Tema.Texto,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                    }

                    TextRenderer.DrawText(g, NomesDosMeses[mes], rotulo,
                        new Rectangle(x - 4, baseY + 4, larguraDaBarra + 8, 16), Tema.TextoFraco,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                }

                return baseY + 44;
            }

            /// <summary>
            /// Mapa de calor do ano: uma coluna por semana, uma linha por dia da semana.
            ///
            /// A primeira coluna começa no início da semana do dia 1º de janeiro, e não no
            /// próprio dia 1º — senão as linhas deixariam de significar "segunda", "terça" e
            /// o mapa perderia o único eixo que ele tem.
            /// </summary>
            private int DesenharMapaDeCalor(Graphics g, Font rotulo, int largura, int y)
            {
                var dias = _dono._estatisticas.SegundosPorDia(_dono.Ano);

                using (var forte = new Font("Segoe UI", 9.75f, FontStyle.Bold))
                    Titulo(g, forte, "Mapa de calor do ano", largura, ref y);

                var primeiroDeJaneiro = new DateTime(_dono.Ano, 1, 1);
                var inicio = EstatisticasDeSessoes.InicioDaSemana(primeiroDeJaneiro);
                var fim = new DateTime(_dono.Ano, 12, 31);

                var semanas = (int)Math.Ceiling(((fim - inicio).Days + 1) / 7.0);
                var reservaDoRotulo = 30;

                var lado = Math.Max(6, Math.Min(14, (largura - reservaDoRotulo - semanas) / Math.Max(1, semanas) - 1));
                var passo = lado + 2;

                var maior = 0;
                foreach (var par in dias) maior = Math.Max(maior, par.Value);

                var esquerda = Margem + reservaDoRotulo;

                for (var semana = 0; semana < semanas; semana++)
                {
                    for (var dia = 0; dia < 7; dia++)
                    {
                        var data = inicio.AddDays((semana * 7) + dia);
                        if (data.Year != _dono.Ano) continue;

                        var area = new Rectangle(esquerda + (semana * passo), y + (dia * passo), lado, lado);

                        dias.TryGetValue(data, out var segundos);

                        using (var pincel = new SolidBrush(CorDoDia(segundos, maior)))
                            g.FillRectangle(pincel, area);
                    }
                }

                // Os rótulos dos dias, um sim um não, para não virar um borrão de texto.
                var cultura = CultureInfo.CurrentCulture;
                var primeiroDia = (int)cultura.DateTimeFormat.FirstDayOfWeek;

                for (var dia = 0; dia < 7; dia += 2)
                {
                    var nome = cultura.DateTimeFormat.AbbreviatedDayNames[(primeiroDia + dia) % 7];

                    TextRenderer.DrawText(g, nome, rotulo,
                        new Rectangle(Margem, y + (dia * passo) - 2, reservaDoRotulo - 4, lado + 4),
                        Tema.TextoFraco, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                }

                var alturaDoMapa = (7 * passo);

                TextRenderer.DrawText(g, "Quanto mais claro, mais tempo naquele dia.", rotulo,
                    new Rectangle(Margem, y + alturaDoMapa + 6, largura, 16), Tema.TextoFraco,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding);

                return y + alturaDoMapa + 42;
            }

            private static Color CorDoDia(int segundos, int maior)
            {
                if (segundos <= 0) return Tema.Superficie;

                // Escala de raiz, não linear: um dia de maratona não pode achatar todos os
                // outros até virarem o mesmo tom do dia sem jogo.
                var proporcao = maior <= 0 ? 0 : Math.Sqrt(segundos / (double)maior);
                var mistura = 0.25 + (0.75 * proporcao);

                return Color.FromArgb(
                    (int)Math.Round(Tema.Superficie.R + ((Tema.Acento.R - Tema.Superficie.R) * mistura)),
                    (int)Math.Round(Tema.Superficie.G + ((Tema.Acento.G - Tema.Superficie.G) * mistura)),
                    (int)Math.Round(Tema.Superficie.B + ((Tema.Acento.B - Tema.Superficie.B) * mistura)));
            }

            /// <summary>Top 10 do histórico inteiro, com barra proporcional ao primeiro.</summary>
            private int DesenharTop(Graphics g, Font rotulo, Font valor, Font forte, int largura, int y)
            {
                var top = _dono._estatisticas.Top(10);

                Titulo(g, forte, "Os 10 mais jogados (desde que existe histórico)", largura, ref y);

                if (top.Count == 0) return y + 20;

                var maior = top[0].Segundos;
                const int AlturaDaLinha = 26;
                var larguraDoNome = Math.Max(140, (int)(largura * 0.34));
                var larguraDoTempo = 92;
                var larguraDaBarra = Math.Max(40, largura - larguraDoNome - larguraDoTempo - 24);

                for (var i = 0; i < top.Count; i++)
                {
                    var linha = y + (i * AlturaDaLinha);
                    var item = top[i];

                    TextRenderer.DrawText(g, item.Jogo.Titulo, valor,
                        new Rectangle(Margem, linha, larguraDoNome, AlturaDaLinha), Tema.Texto,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                        TextFormatFlags.EndEllipsis);

                    var trilho = new Rectangle(Margem + larguraDoNome + 12, linha + 7, larguraDaBarra, 12);
                    using (var pincel = new SolidBrush(Tema.Superficie))
                        g.FillRectangle(pincel, trilho);

                    var comprimento = maior <= 0
                        ? 0
                        : Math.Max(2, (int)Math.Round(item.Segundos / (double)maior * trilho.Width));

                    using (var pincel = new SolidBrush(Tema.Acento))
                        g.FillRectangle(pincel, new Rectangle(trilho.X, trilho.Y, comprimento, trilho.Height));

                    TextRenderer.DrawText(g, TempoDeJogo.DescreverCurto(item.Segundos), rotulo,
                        new Rectangle(trilho.Right + 12, linha, larguraDoTempo, AlturaDaLinha), Tema.TextoForte,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }

                var fim = y + (top.Count * AlturaDaLinha) + 16;

                if (_dono._estatisticas.Orfas > 0)
                {
                    TextRenderer.DrawText(g,
                        $"{_dono._estatisticas.Orfas} sessão(ões) de jogo que não está mais na biblioteca " +
                        "foram ignoradas — o registro delas continua no arquivo.",
                        rotulo, new Rectangle(Margem, fim, largura, 18), Tema.TextoFraco,
                        TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

                    fim += 24;
                }

                return fim;
            }

            private static void Titulo(Graphics g, Font fonte, string texto, int largura, ref int y)
            {
                TextRenderer.DrawText(g, texto, fonte, new Rectangle(Margem, y, largura, 22), Tema.TextoForte,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                y += 30;
            }
        }
    }
}
