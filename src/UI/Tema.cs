using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Mochila.Modelo;

namespace Mochila.UI
{
    /// <summary>
    /// A paleta do launcher, num lugar só.
    ///
    /// Cinza puro deixa a grade com cara de caixa de diálogo; o escuro daqui é levemente
    /// azulado, e as capas — que são o conteúdo — ficam sendo a única coisa colorida da
    /// tela. O tema claro (fase 17) segue a mesma ideia pelo avesso: fundo cinza-azulado
    /// bem claro, superfícies brancas, e a arte continuando a ser o que salta.
    ///
    /// <b>As cores são propriedades, e não mais constantes.</b> Foi o que a fase 17 mudou:
    /// dois presets e uma cor de acento vinda do <c>config.json</c>. Em troca, existe uma
    /// regra nova e ela é dura — <see cref="Aplicar(Config)"/> tem que rodar ANTES do
    /// primeiro controle nascer. Cada controle copia <c>BackColor</c> e <c>ForeColor</c> no
    /// próprio construtor, então trocar a paleta com a janela montada não repinta ninguém.
    /// É por isso que a troca de tema na tela de configurações se oferece a reabrir o
    /// launcher em vez de fingir que aplicou.
    /// </summary>
    public static class Tema
    {
        /// <summary>Os dois presets da fase 17. Sem editor de tema: dois bastam.</summary>
        public enum Paleta
        {
            Escuro = 0,
            Claro = 1
        }

        /// <summary>Qual preset está no ar. Muda só em <see cref="Aplicar(Config)"/>.</summary>
        public static Paleta Atual { get; private set; } = Paleta.Escuro;

        public static bool EhEscuro => Atual == Paleta.Escuro;

        /// <summary>
        /// O que deu errado ao ler o tema do <c>config.json</c>, ou null quando estava tudo
        /// bem. A janela mostra isto no rodapé depois de carregar.
        ///
        /// <b>Nada aqui impede a abertura</b>, e é regra da spec: cor de acento sem sentido,
        /// string vazia ou campo ausente caem no padrão do tema e viram um recado, nunca
        /// uma caixa de erro na frente de quem só queria abrir um jogo.
        /// </summary>
        public static string? Aviso { get; private set; }

        // ---- Superfícies ---------------------------------------------------------------------

        /// <summary>Fundo da grade e das janelas.</summary>
        public static Color Fundo { get; private set; }

        /// <summary>Barra superior, rodapé, menus: um degrau afastado do fundo.</summary>
        public static Color Superficie { get; private set; }

        /// <summary>Campo de busca, combo, botão: o degrau em que se clica.</summary>
        public static Color Controle { get; private set; }

        /// <summary>O mesmo controle sob o mouse.</summary>
        public static Color ControleAceso { get; private set; }

        /// <summary>Borda discreta que separa um controle do painel atrás dele.</summary>
        public static Color Borda { get; private set; }

        /// <summary>Borda de quem está sob o mouse — clara, mas ainda não é seleção.</summary>
        public static Color BordaClara { get; private set; }

        /// <summary>
        /// O controle que está LIGADO: chip de tag ativa, filtro de favoritos marcado, item
        /// de menu sob o cursor, opção destacada no combo.
        ///
        /// É um verde lavado, não o acento berrante: são superfícies grandes, e pintá-las
        /// com a cor de destaque cheia deixaria a barra parecendo um erro. Virou cor de tema
        /// na fase 17 — antes era um literal repetido em seis lugares, todos calibrados para
        /// o fundo escuro, e todos os seis eram uma tarja escura no tema claro.
        /// </summary>
        public static Color ControleMarcado { get; private set; }

        // ---- Texto ----------------------------------------------------------------------------

        public static Color Texto { get; private set; }

        /// <summary>Texto que precisa saltar: título selecionado, nome do jogo no rodapé.</summary>
        public static Color TextoForte { get; private set; }

        public static Color TextoFraco { get; private set; }

        public static Color Erro { get; private set; }

        // ---- Destaque -------------------------------------------------------------------------

        /// <summary>
        /// O verde-lima da seleção, que virou o destaque de tudo: foco, borda e botão
        /// principal. É a cor do ícone da mochila, para o launcher e o ícone serem uma
        /// identidade só.
        ///
        /// É a única cor que o <c>config.json</c> deixa trocar. Todo o resto vem do preset —
        /// um editor de paleta completo daria trabalho para produzir, na prática, temas
        /// ilegíveis.
        /// </summary>
        public static Color Acento { get; private set; }

        /// <summary>Contorno do card selecionado na grade.</summary>
        public static Color Selecao => Acento;

        /// <summary>Faixa do jogo em execução.</summary>
        public static Color Sucesso { get; private set; }

        // ---- Janela de revisão do scan --------------------------------------------------------

        /// <summary>Linha de baixa confiança (placar &lt; 30) na janela de revisão.</summary>
        public static Color FundoBaixaConfianca { get; private set; }

        public static Color TextoBaixaConfianca { get; private set; }

        /// <summary>Candidato vetado (instalador, redistribuível): visível, mas apagado.</summary>
        public static Color TextoExcluido { get; private set; }

        // ---- Escolha do tema ------------------------------------------------------------------

        static Tema() => AplicarEscuro();

        /// <summary>
        /// Põe no ar o tema pedido pelo <c>config.json</c>.
        ///
        /// Chamada uma vez, cedo, antes de existir controle. Ver o comentário da classe para
        /// o porquê de isso não ser detalhe.
        /// </summary>
        public static void Aplicar(Config config)
        {
            if (config is null) throw new ArgumentNullException(nameof(config));

            Aplicar(config.Tema == TemaDoLauncher.Claro ? Paleta.Claro : Paleta.Escuro,
                    config.CorDeAcento);
        }

        /// <summary>A mesma coisa sem depender do arquivo. Existe para o <c>--autoteste</c>.</summary>
        public static void Aplicar(Paleta paleta, string? corDeAcento)
        {
            Atual = paleta;
            Aviso = null;

            if (paleta == Paleta.Claro) AplicarClaro();
            else AplicarEscuro();

            // Campo ausente ou vazio é o caso normal — significa "use o acento do preset",
            // e não é erro nenhum. Só texto PRESENTE e sem sentido vira aviso.
            if (string.IsNullOrWhiteSpace(corDeAcento)) return;

            if (TentarLerCor(corDeAcento, out var cor))
            {
                Acento = cor;
                return;
            }

            Aviso = $"Não entendi a cor de acento \"{corDeAcento!.Trim()}\" do config.json " +
                    "(o formato é #RRGGBB, tipo #A8FF3E). Usei a cor padrão do tema.";
        }

        /// <summary>
        /// Lê <c>#RRGGBB</c> (com ou sem o <c>#</c>) e também os nomes que o .NET conhece
        /// ("SteelBlue"), que é o que alguém tenta escrever à mão no arquivo.
        ///
        /// Recusa o que não vira cor. Recusa também o que vira cor transparente: um acento
        /// com alfa zero seria uma cor válida e invisível, que é pior que um erro — a tela
        /// abriria sem borda de seleção e sem nada explicando o motivo.
        /// </summary>
        public static bool TentarLerCor(string? texto, out Color cor)
        {
            cor = Color.Empty;
            if (string.IsNullOrWhiteSpace(texto)) return false;

            var limpo = texto!.Trim();

            if (limpo.StartsWith("#", StringComparison.Ordinal)) limpo = limpo.Substring(1);

            if (limpo.Length == 6 &&
                int.TryParse(limpo, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                cor = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
                return true;
            }

            // Só tenta o nome quando não parecia hexadecimal: "ABC" é nome inválido, e
            // deixar o ColorTranslator opinar sobre isso só embaralharia a mensagem de erro.
            if (limpo.Length == 0 || !EhSoLetra(limpo)) return false;

            try
            {
                var nomeada = ColorTranslator.FromHtml(limpo);
                if (nomeada.A == 0) return false;

                cor = Color.FromArgb(nomeada.R, nomeada.G, nomeada.B);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Como a cor vai para o <c>config.json</c>: sempre <c>#RRGGBB</c>.</summary>
        public static string FormatarCor(Color cor)
            => $"#{cor.R:X2}{cor.G:X2}{cor.B:X2}";

        private static bool EhSoLetra(string texto)
        {
            foreach (var c in texto)
            {
                if (!char.IsLetter(c)) return false;
            }
            return true;
        }

        private static void AplicarEscuro()
        {
            Fundo = Color.FromArgb(0x16, 0x18, 0x1D);
            Superficie = Color.FromArgb(0x1E, 0x21, 0x2A);
            Controle = Color.FromArgb(0x26, 0x2A, 0x35);
            ControleAceso = Color.FromArgb(0x2F, 0x34, 0x41);
            Borda = Color.FromArgb(0x2E, 0x34, 0x40);
            BordaClara = Color.FromArgb(0x4A, 0x52, 0x63);
            ControleMarcado = Color.FromArgb(44, 62, 38);

            Texto = Color.FromArgb(0xC8, 0xCD, 0xD6);
            TextoForte = Color.FromArgb(0xE8, 0xEC, 0xF2);
            TextoFraco = Color.FromArgb(0x7E, 0x87, 0x94);
            Erro = Color.FromArgb(230, 120, 120);

            Acento = Color.FromArgb(0xA8, 0xFF, 0x3E);
            Sucesso = Color.FromArgb(62, 190, 130);

            FundoBaixaConfianca = Color.FromArgb(92, 76, 16);
            TextoBaixaConfianca = Color.FromArgb(255, 230, 150);
            TextoExcluido = Color.FromArgb(130, 110, 110);
        }

        /// <summary>
        /// O tema claro. Não é o escuro invertido canal a canal — isso produz um cinza
        /// sujo e um acento fluorescente.
        ///
        /// A regra que vale nos dois é a mesma: o fundo é levemente azulado, as superfícies
        /// se afastam dele em um degrau, e a arte das capas continua sendo a única coisa
        /// realmente colorida. O que muda é a direção do degrau — no escuro a superfície
        /// sobe, no claro ela branqueia.
        /// </summary>
        private static void AplicarClaro()
        {
            Fundo = Color.FromArgb(0xF2, 0xF4, 0xF8);
            Superficie = Color.FromArgb(0xFF, 0xFF, 0xFF);
            Controle = Color.FromArgb(0xE6, 0xE9, 0xF0);
            ControleAceso = Color.FromArgb(0xDA, 0xDF, 0xE9);
            Borda = Color.FromArgb(0xCF, 0xD4, 0xDF);
            BordaClara = Color.FromArgb(0x9A, 0xA3, 0xB4);
            ControleMarcado = Color.FromArgb(0xDE, 0xEF, 0xCC);

            Texto = Color.FromArgb(0x33, 0x38, 0x43);
            TextoForte = Color.FromArgb(0x14, 0x17, 0x1C);
            TextoFraco = Color.FromArgb(0x6B, 0x74, 0x84);
            Erro = Color.FromArgb(186, 58, 58);

            // O lima do tema escuro some sobre fundo claro (contraste perto de 1,2): aqui o
            // acento é o mesmo verde, escurecido até passar do piso de 4,5.
            Acento = Color.FromArgb(0x2F, 0x7D, 0x12);
            Sucesso = Color.FromArgb(22, 132, 84);

            FundoBaixaConfianca = Color.FromArgb(255, 244, 198);
            TextoBaixaConfianca = Color.FromArgb(112, 84, 8);
            TextoExcluido = Color.FromArgb(154, 138, 138);
        }

        // ---- Janelas --------------------------------------------------------------------------

        /// <summary>
        /// Veste uma janela com o tema: ícone da mochila e barra de título na cor certa.
        ///
        /// A barra de título é do Windows, não nossa — sem o aviso ao DWM, uma faixa branca
        /// fica em cima de uma janela preta (e, no tema claro, uma faixa preta em cima de
        /// uma janela branca). O atributo é ignorado em Windows antigo, e falhar aqui não
        /// pode impedir a janela de abrir.
        /// </summary>
        public static void AplicarNaJanela(Form janela)
        {
            janela.BackColor = Fundo;
            janela.ForeColor = Texto;

            if (IconeDaMochila.DaJanela is { } icone) janela.Icon = icone;

            janela.HandleCreated += (_, _) => PintarBarraDeTitulo(janela.Handle);
            if (janela.IsHandleCreated) PintarBarraDeTitulo(janela.Handle);
        }

        private const int ModoEscuro = 20;
        private const int ModoEscuroAntesDo20H1 = 19;

        private static void PintarBarraDeTitulo(IntPtr janela)
        {
            var ligado = EhEscuro ? 1 : 0;

            try
            {
                if (DwmSetWindowAttribute(janela, ModoEscuro, ref ligado, sizeof(int)) != 0)
                    DwmSetWindowAttribute(janela, ModoEscuroAntesDo20H1, ref ligado, sizeof(int));
            }
            catch (Exception)
            {
                // Windows sem DWM (ou sessão sem composição): a barra fica no padrão e pronto.
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr janela, int atributo, ref int valor, int tamanho);
    }

    /// <summary>
    /// Geometria repetida no desenho à mão. O retângulo arredondado aparece no card, no
    /// botão, no campo de busca e no combo — vale ter um lugar só que saiba fazê-lo.
    /// </summary>
    public static class Formas
    {
        public static GraphicsPath Arredondado(Rectangle area, int raio)
        {
            var caminho = new GraphicsPath();

            // Área degenerada (controle ainda sem tamanho) viraria arco inválido no GDI+.
            if (area.Width <= 0 || area.Height <= 0) return caminho;

            var limite = Math.Max(1, Math.Min(raio, Math.Min(area.Width, area.Height) / 2));
            var d = limite * 2;

            caminho.AddArc(area.X, area.Y, d, d, 180, 90);
            caminho.AddArc(area.Right - d, area.Y, d, d, 270, 90);
            caminho.AddArc(area.Right - d, area.Bottom - d, d, d, 0, 90);
            caminho.AddArc(area.X, area.Bottom - d, d, d, 90, 90);
            caminho.CloseFigure();

            return caminho;
        }
    }
}
