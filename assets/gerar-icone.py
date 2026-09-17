"""Gera a arte do icone do Mochila (identidade "Verde Lima") a partir de codigo.

    python assets\\gerar-icone.py

Escreve:
    assets\\mochila-fonte.png     1024 px, a arte de trabalho
    assets\\mochila.png           256 px, o recurso embutido no exe
    assets\\github-preview.png    1280x640, imagem de preview do repositorio

Depois de regerar, atualize o .ico do exe pela build de Debug:
    bin\\Debug\\Mochila.exe --gerar-icone D:\\caminho\\para\\mochila.ico

Direcao: mochila em silhueta chapada verde-lima sobre placa grafite. Topo em cupula,
alcas saindo pelas laterais, pega no alto, aba separada por um vinco e um bolso com
controle. Tudo grosso o bastante para continuar legivel em 16 px, que e o tamanho da
barra de titulo.
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

S = 1024
PASTA = os.path.dirname(os.path.abspath(__file__))

LIMA_CLARO = (0xC2, 0xFF, 0x5E)
LIMA = (0xA8, 0xFF, 0x3E)
LIMA_FUNDO = (0x8A, 0xE8, 0x22)
LIMA_SOMBRA = (0x5E, 0xB0, 0x14)
PLACA_TOPO = (0x1F, 0x26, 0x1D)
PLACA_BASE = (0x0D, 0x11, 0x0D)
TINTA = (0x12, 0x17, 0x12)


# ---------------------------------------------------------------- utilitarios

def mascara():
    return Image.new("L", (S, S), 0)


def rrect(box, r, m=None):
    m = m or mascara()
    ImageDraw.Draw(m).rounded_rectangle(box, radius=r, fill=255)
    return m


def uniao(*ms):
    a = np.array(ms[0])
    for m in ms[1:]:
        a = np.maximum(a, np.array(m))
    return Image.fromarray(a)


def menos(a, b):
    return Image.fromarray(np.clip(np.array(a).astype(np.int16) - np.array(b), 0, 255).astype(np.uint8))


def gradiente_vertical(y0, y1, c0, c1):
    t = np.clip((np.arange(S) - y0) / float(y1 - y0), 0, 1)[:, None, None]
    arr = np.array(c0, float) * (1 - t) + np.array(c1, float) * t
    return Image.fromarray(np.repeat(arr, S, axis=1).astype(np.uint8), "RGB")


def pinta(base, m, fonte):
    camada = fonte if isinstance(fonte, Image.Image) else Image.new("RGB", (S, S), fonte)
    base.paste(camada, (0, 0), m)


def reduz(img, lado):
    """Reducao com alfa pre-multiplicado: sem isso sobra halo no contorno."""
    f = np.array(img).astype(np.float64)
    f[:, :, :3] *= f[:, :, 3:4] / 255.0
    p = np.array(Image.fromarray(f.astype(np.uint8)).resize((lado, lado), Image.LANCZOS)).astype(np.float64)
    al = np.clip(p[:, :, 3:4], 1, 255)
    p[:, :, :3] = np.clip(p[:, :, :3] * 255.0 / al, 0, 255)
    return Image.fromarray(p.astype(np.uint8))


# ---------------------------------------------------------------- geometria

def placa():
    return rrect((20, 20, S - 20, S - 20), 232)


def corpo():
    """Topo em cupula e base arredondada: cupula e o traco que separa mochila de mala."""
    x0, x1, topo, base = 262, 762, 250, 872
    raio = (x1 - x0) // 2
    m = mascara()
    ImageDraw.Draw(m).ellipse((x0, topo, x1, topo + raio * 2), fill=255)
    rrect((x0, topo + raio, x1, base), 104, m)
    return m


def alcas():
    return uniao(rrect((196, 440, 300, 812), 52), rrect((724, 440, 828, 812), 52))


def pega():
    m = mascara()
    d = ImageDraw.Draw(m)
    d.arc((420, 150, 604, 334), 180, 360, fill=255, width=52)
    d.rectangle((420, 240, 472, 300), fill=255)
    d.rectangle((552, 240, 604, 300), fill=255)
    return m


def vinco():
    """Linha da aba: arco raso que desce no centro."""
    m = mascara()
    ImageDraw.Draw(m).arc((236, 300, 788, 596), 18, 162, fill=255, width=30)
    return m


def fivela():
    return rrect((470, 560, 554, 624), 22)


def bolso():
    return rrect((340, 660, 684, 836), 64)


def controle():
    """D-pad a esquerda, dois botoes a direita, no centro do bolso."""
    m = mascara()
    d = ImageDraw.Draw(m)
    cx, cy = 440, 748
    d.rounded_rectangle((cx - 58, cy - 20, cx + 58, cy + 20), 10, fill=255)
    d.rounded_rectangle((cx - 20, cy - 58, cx + 20, cy + 58), 10, fill=255)
    for bx, by in ((590, 718), (624, 780)):
        d.ellipse((bx - 24, by - 24, bx + 24, by + 24), fill=255)
    return m


# ---------------------------------------------------------------- montagem

def icone():
    base = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    m_placa = placa()
    pinta(base, m_placa, gradiente_vertical(20, S - 20, PLACA_TOPO, PLACA_BASE))
    # fio de luz na borda de cima da placa
    fio = menos(m_placa, rrect((20, 26, S - 20, S - 20), 232))
    camada = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    camada.putalpha(fio.point(lambda v: int(v * 0.10)))
    base = Image.alpha_composite(base, camada)

    # sombra do desenho sobre a placa
    silhueta = uniao(corpo(), alcas(), pega())
    desenho = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sombra = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sombra.putalpha(silhueta.filter(ImageFilter.GaussianBlur(28)).point(lambda v: int(v * 0.55)))
    desenho.alpha_composite(sombra, (0, 22))

    pinta(desenho, alcas(), LIMA_SOMBRA)
    pinta(desenho, pega(), LIMA_FUNDO)
    pinta(desenho, corpo(), gradiente_vertical(250, 872, LIMA_CLARO, LIMA_FUNDO))
    pinta(desenho, vinco(), TINTA)
    pinta(desenho, fivela(), TINTA)
    pinta(desenho, rrect((486, 574, 538, 610), 12), LIMA)
    pinta(desenho, bolso(), TINTA)
    pinta(desenho, controle(), LIMA)

    # A geometria acima deixa folga demais dentro da placa; em 16 px a mochila sumia.
    # Amplia o desenho em torno do centro dele antes de assentar na placa.
    escala = 1.16
    lado = int(S * escala)
    ampliado = desenho.resize((lado, lado), Image.LANCZOS)
    centro_y = int(511 * escala)
    base.alpha_composite(ampliado.crop(((lado - S) // 2, centro_y - S // 2 - 6,
                                        (lado - S) // 2 + S, centro_y + S // 2 - 6)))
    return base


def preview_github(arte):
    W, H = 1280, 640
    fundo = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(fundo)
    for y in range(H):
        t = y / H
        d.line([(0, y), (W, y)], fill=tuple(int(a * (1 - t) + b * t) for a, b in zip((20, 25, 19), (9, 11, 9))))
    brilho = Image.new("L", (W, H), 0)
    ImageDraw.Draw(brilho).ellipse((40, 60, 620, 640), fill=110)
    brilho = brilho.filter(ImageFilter.GaussianBlur(150))
    fundo = Image.composite(Image.new("RGB", (W, H), LIMA), fundo, brilho.point(lambda v: int(v * 0.35)))
    fundo = fundo.convert("RGBA")

    lado = 330
    fundo.alpha_composite(reduz(arte, lado), (140, (H - lado) // 2))

    fontes = "C:/Windows/Fonts/"
    marca = ImageFont.truetype(fontes + "bahnschrift.ttf", 150)
    marca.set_variation_by_name("Bold Condensed")
    sub = ImageFont.truetype(fontes + "bahnschrift.ttf", 34)
    sub.set_variation_by_name("SemiBold")
    texto = ImageFont.truetype(fontes + "segoeui.ttf", 30)
    d = ImageDraw.Draw(fundo)
    x = 540
    d.text((x - 6, 238), "MOCHILA", font=marca, fill=LIMA, anchor="ls")
    d.text((x, 292), "L A U N C H E R", font=sub, fill=(214, 224, 208), anchor="ls")
    d.text((x, 368), "O launcher portátil que mora no HD", font=texto, fill=(196, 204, 190), anchor="ls")
    d.text((x, 408), "junto com os seus jogos.", font=texto, fill=(196, 204, 190), anchor="ls")

    chip = ImageFont.truetype(fontes + "segoeuib.ttf", 22)
    cx = x
    for rotulo in ("~400 KB", "sem instalar", "Windows 10 | 11"):
        largura = d.textlength(rotulo, font=chip) + 36
        d.rounded_rectangle((cx, 452, cx + largura, 494), 21, outline=(90, 120, 70), width=2)
        d.text((cx + largura / 2, 473), rotulo, font=chip, fill=(214, 224, 208), anchor="mm")
        cx += largura + 12
    return fundo.convert("RGB")


def banner_readme(arte, print_da_grade):
    """Faixa de abertura do README: marca à esquerda, a grade de verdade à direita."""
    W, H = 1800, 640
    fundo = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(fundo)
    for y in range(H):
        t = y / H
        d.line([(0, y), (W, y)], fill=tuple(int(a * (1 - t) + b * t) for a, b in zip((19, 24, 18), (8, 10, 8))))
    brilho = Image.new("L", (W, H), 0)
    ImageDraw.Draw(brilho).ellipse((-200, -120, 900, 760), fill=120)
    brilho = brilho.filter(ImageFilter.GaussianBlur(170))
    fundo = Image.composite(Image.new("RGB", (W, H), LIMA), fundo, brilho.point(lambda v: int(v * 0.30)))
    fundo = fundo.convert("RGBA")

    # A grade entra pela direita, cortada na borda, e some num degradê antes do texto.
    grade = Image.open(print_da_grade).convert("RGB")
    largura = 1180
    grade = grade.resize((largura, int(grade.height * largura / grade.width)), Image.LANCZOS)
    janela = Image.new("RGBA", grade.size, (0, 0, 0, 0))
    mascara_janela = Image.new("L", grade.size, 0)
    ImageDraw.Draw(mascara_janela).rounded_rectangle((0, 0, grade.width - 1, grade.height - 1), 18, fill=255)
    janela.paste(grade, (0, 0), mascara_janela)
    x0, y0 = 860, 90
    borda = Image.new("RGBA", grade.size, (0, 0, 0, 0))
    ImageDraw.Draw(borda).rounded_rectangle((0, 0, grade.width - 1, grade.height - 1), 18, outline=(255, 255, 255, 36), width=2)
    janela.alpha_composite(borda)

    # Em vez de um véu por cima (que deixava uma emenda contra o brilho do fundo), a
    # própria janela ganha transparência crescente na borda esquerda.
    rampa = Image.new("L", grade.size, 255)
    rd_ = ImageDraw.Draw(rampa)
    for x in range(360):
        rd_.line([(x, 0), (x, grade.height)], fill=int(255 * (x / 360) ** 1.8))
    alfa = Image.fromarray(np.minimum(np.array(janela.getchannel("A")), np.array(rampa)))
    janela.putalpha(alfa)

    sombra = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    camada_sombra = Image.new("RGBA", grade.size, (0, 0, 0, 0))
    camada_sombra.putalpha(alfa.point(lambda v: int(v * 0.8)))
    sombra.alpha_composite(camada_sombra, (x0, y0 + 30))
    fundo.alpha_composite(sombra.filter(ImageFilter.GaussianBlur(40)))
    fundo.alpha_composite(janela, (x0, y0))

    rodape = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    rd = ImageDraw.Draw(rodape)
    for y in range(H - 200, H):
        rd.line([(0, y), (W, y)], fill=(8, 10, 8, int(230 * ((y - (H - 200)) / 200) ** 1.5)))
    fundo.alpha_composite(rodape)

    fontes = "C:/Windows/Fonts/"
    marca = ImageFont.truetype(fontes + "bahnschrift.ttf", 168)
    marca.set_variation_by_name("Bold Condensed")
    sub = ImageFont.truetype(fontes + "bahnschrift.ttf", 36)
    sub.set_variation_by_name("SemiBold")
    texto = ImageFont.truetype(fontes + "segoeui.ttf", 34)
    chip = ImageFont.truetype(fontes + "segoeuib.ttf", 24)

    lado = 170
    fundo.alpha_composite(reduz(arte, lado), (110, 118))
    d = ImageDraw.Draw(fundo)
    x = 110
    d.text((x + lado + 30, 268), "MOCHILA", font=marca, fill=LIMA, anchor="ls")
    d.text((x + lado + 36, 318), "L A U N C H E R", font=sub, fill=(214, 224, 208), anchor="ls")
    d.text((x, 398), "Seus jogos num HD externo,", font=texto, fill=(222, 230, 216), anchor="ls")
    d.text((x, 444), "abertos com um clique em qualquer PC.", font=texto, fill=(222, 230, 216), anchor="ls")

    cx = x
    for rotulo in ("1 arquivo · 444 KB", "sem instalar", "Windows 10 | 11"):
        largura_chip = d.textlength(rotulo, font=chip) + 40
        d.rounded_rectangle((cx, 492, cx + largura_chip, 540), 24, fill=(22, 30, 20), outline=(96, 130, 70), width=2)
        d.text((cx + largura_chip / 2, 516), rotulo, font=chip, fill=(214, 224, 208), anchor="mm")
        cx += largura_chip + 14
    return fundo.convert("RGB")


def main():
    arte = icone()
    arte.save(os.path.join(PASTA, "mochila-fonte.png"), optimize=True)
    reduz(arte, 256).save(os.path.join(PASTA, "mochila.png"), optimize=True)
    preview_github(arte).save(os.path.join(PASTA, "github-preview.png"), optimize=True)
    print("ok: mochila-fonte.png, mochila.png, github-preview.png")

    # O banner usa o print da grade (docs/grade.png), que sai do --captura. Sem o print,
    # o resto da arte continua sendo gerado.
    docs = os.path.join(os.path.dirname(PASTA), "docs")
    grade = os.path.join(docs, "grade.png")
    if os.path.exists(grade):
        banner_readme(arte, grade).save(os.path.join(docs, "banner.png"), optimize=True)
        print("ok: docs/banner.png")


if __name__ == "__main__":
    main()
