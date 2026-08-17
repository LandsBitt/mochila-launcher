"""Gera a arte do icone do Mochila a partir de codigo, em vez de arte crua do gerador.

    python assets\\gerar-icone.py

Escreve assets\\propostas\\<variante>-<tamanho>.png para comparacao.

Direcao: silhueta unica (sem linha interna, sem ilustracao), corpo em aco escovado com
gradiente multi-parada, e um recorte por onde vaza luz ambar. O metal fica em tom medio,
nao escuro: silhueta escura recortada some na barra de tarefas escura do Windows.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 1024  # canvas de trabalho; toda geometria abaixo esta nesta escala
SAIDA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "propostas")

CONTORNO = (10, 12, 18)


# ---------------------------------------------------------------- utilitarios

def mascara():
    return Image.new("L", (S, S), 0)


def rrect(m, box, r):
    ImageDraw.Draw(m).rounded_rectangle(box, radius=r, fill=255)
    return m


def uniao(*ms):
    a = np.array(ms[0])
    for m in ms[1:]:
        a = np.maximum(a, np.array(m))
    return Image.fromarray(a)


def menos(a, b):
    return Image.fromarray(np.clip(np.array(a).astype(np.int16) - np.array(b), 0, 255).astype(np.uint8))


def dentro(a, b):
    return Image.fromarray(np.minimum(np.array(a), np.array(b)))


def engorda(m, px):
    """Dilatacao redonda: borra e corta. MaxFilter engordaria em quadrado."""
    return m.filter(ImageFilter.GaussianBlur(px * 0.62)).point(lambda v: 255 if v > 96 else 0)


def move(m, dx, dy):
    return m.transform(m.size, Image.AFFINE, (1, 0, -dx, 0, 1, -dy))


def grad(p0, p1, paradas):
    """Gradiente linear com varias paradas. Metal precisa de rampa quebrada, nao de
    dois pontos: e a inversao brusca no meio que o olho le como reflexo."""
    ys, xs = np.mgrid[0:S, 0:S].astype(np.float64)
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    t = np.clip(((xs - p0[0]) * dx + (ys - p0[1]) * dy) / float(dx * dx + dy * dy), 0.0, 1.0)
    ts = np.array([p[0] for p in paradas], float)
    cs = np.array([p[1] for p in paradas], float)
    arr = np.stack([np.interp(t, ts, cs[:, c]) for c in range(3)], axis=-1)
    return Image.fromarray(arr.astype(np.uint8), "RGB")


def pinta(base, m, fonte):
    base.paste(fonte if isinstance(fonte, Image.Image) else Image.new("RGB", (S, S), fonte), (0, 0), m)


def veu(base, m, cor, forca, borrar=0):
    """Sobrepoe uma cor translucida atraves da mascara m."""
    a = m.filter(ImageFilter.GaussianBlur(borrar)) if borrar else m
    camada = Image.new("RGBA", (S, S), cor + (0,))
    camada.putalpha(a.point(lambda v: int(v * forca)))
    return Image.alpha_composite(base, camada)


def reduz(img, lado):
    """Reducao com alfa pre-multiplicado: sem isso sobra halo no contorno."""
    f = np.array(img).astype(np.float64)
    f[:, :, :3] *= f[:, :, 3:4] / 255.0
    p = np.array(Image.fromarray(f.astype(np.uint8)).resize((lado, lado), Image.LANCZOS)).astype(np.float64)
    al = np.clip(p[:, :, 3:4], 1, 255)
    p[:, :, :3] = np.clip(p[:, :, :3] * 255.0 / al, 0, 255)
    return Image.fromarray(p.astype(np.uint8))


# ---------------------------------------------------------------- materiais

# Aco escovado: claro no topo, queda rapida, banda clara na linha do horizonte,
# escuro embaixo e um rebote de luz na base. As paradas 0.46/0.54 sao a virada.
# Mapeado sobre o bolso (y 600 a 915), nao sobre o corpo inteiro: e a faixa que fica
# visivel abaixo da aba, e e nela que a virada de reflexo precisa cair.
ACO = grad((0, 600), (0, 916), [
    (0.00, (0xB4, 0xBF, 0xD4)),
    (0.16, (0x8C, 0x97, 0xAC)),
    (0.40, (0x5E, 0x69, 0x7C)),
    (0.50, (0x39, 0x41, 0x50)),
    (0.58, (0x8E, 0x9A, 0xB0)),
    (0.80, (0x4C, 0x55, 0x66)),
    (1.00, (0x6B, 0x76, 0x8B)),
])

GRAFITE = grad((0, 200), (0, 640), [
    (0.00, (0x8A, 0x96, 0xAC)),
    (0.18, (0x4C, 0x55, 0x67)),
    (0.52, (0x2A, 0x30, 0x3D)),
    (0.62, (0x51, 0x5B, 0x6E)),
    (0.86, (0x1E, 0x23, 0x2E)),
    (1.00, (0x33, 0x3A, 0x49)),
])

FOGO = grad((0, 380), (0, 810), [
    (0.00, (0xFF, 0xF4, 0xD0)),
    (0.22, (0xFF, 0xC2, 0x4A)),
    (0.55, (0xFF, 0x8A, 0x0A)),
    (1.00, (0xE5, 0x3D, 0x00)),
])
AMBAR = (0xFF, 0x8A, 0x14)


# ---------------------------------------------------------------- geometria

def domo(box, r_base):
    """Topo em cupula, base reta. A cupula e o traco que mais identifica mochila:
    mala e cofre tem topo chato, mochila nao."""
    x0, y0, x1, y1 = box
    meio = y0 + (x1 - x0) * 0.62
    m = mascara()
    ImageDraw.Draw(m).pieslice((x0, y0, x1, meio + (meio - y0)), 180, 360, fill=255)
    rrect(m, (x0, meio, x1, y1), r_base)
    return m


def costas(pega=True):
    """Chapa de tras: alcas em arco descendo pelas laterais, mais a pega no topo."""
    partes = [arco((112, 252, 500, 876), 92, 268, 58),
              arco((524, 252, 912, 876), 272, 88, 58)]
    if pega:
        partes.append(rrect(mascara(), (476, 122, 548, 300), 36))
        partes.append(rrect(mascara(), (150, 300, 874, 880), 120))
    else:
        partes.append(rrect(mascara(), (150, 300, 874, 880), 120))
    return uniao(*partes)


def frente():
    return domo((172, 216, 852, 902), 96)


def aba():
    """Aba de fechamento: o terco superior do corpo, cortado por uma curva que desce
    no centro. Junto com a fivela e o que separa mochila de bolsa generica."""
    curva = mascara()
    ImageDraw.Draw(curva).ellipse((84, 96, 940, 616), fill=255)
    return dentro(frente(), curva)


def fivela():
    return rrect(mascara(), (466, 528, 558, 618), 22)


def arco(bbox, ini, fim, largura):
    m = mascara()
    ImageDraw.Draw(m).arc(bbox, ini, fim, fill=255, width=largura)
    return m


def zipper():
    """Vinco do bolso, em arco raso. E o segundo sinal de 'mochila' depois das alcas:
    so a silhueta externa e ambigua entre mochila, mala e cofre."""
    m = mascara()
    ImageDraw.Draw(m).arc((262, 384, 762, 690), 204, 336, fill=255, width=22)
    return m


def cruz(centro, braco, espessura, raio):
    cx, cy = centro
    b, e = braco / 2.0, espessura / 2.0
    m = mascara()
    rrect(m, (cx - b, cy - e, cx + b, cy + e), raio)
    rrect(m, (cx - e, cy - b, cx + e, cy + b), raio)
    return m


def arredonda(m, r):
    return m.filter(ImageFilter.GaussianBlur(r)).point(lambda v: 255 if v > 128 else 0)


def play(centro, tamanho):
    """Triangulo de play. Cruz simetrica sobre uma bolsa le como kit medico; o play
    le como lancar, que e exatamente o que o programa faz."""
    cx, cy = centro
    h = tamanho / 2.0
    l = tamanho * 0.88 / 2.0
    m = mascara()
    ImageDraw.Draw(m).polygon([(cx - l * 0.66, cy - h), (cx + l * 1.0, cy), (cx - l * 0.66, cy + h)], fill=255)
    return arredonda(m, tamanho * 0.075)


def raio(centro, altura):
    """Raio: energia, velocidade, 'launcher'. Diagonal, entao nao compete com a
    simetria da mochila e nunca vira cruz."""
    cx, cy = centro
    u = altura / 100.0
    pts = [(-16, -50), (30, -50), (2, -8), (34, -8), (-22, 50), (-6, 4), (-34, 4)]
    m = mascara()
    ImageDraw.Draw(m).polygon([(cx + x * u, cy + y * u) for x, y in pts], fill=255)
    return arredonda(m, altura * 0.038)


# ---------------------------------------------------------------- montagem

def chapa(base, forma, material, contorno):
    pinta(base, engorda(forma, contorno), CONTORNO)
    pinta(base, forma, material)
    # Chanfro: fio claro na aresta de cima, fio escuro na de baixo. Sao estas duas
    # linhas de poucos pixels que fazem a forma parecer chapa e nao adesivo.
    base = veu(base, dentro(menos(forma, move(forma, 0, 9)), forma), (0xFF, 0xFF, 0xFF), 0.62)
    return veu(base, dentro(menos(forma, move(forma, 0, -11)), forma), (0, 0, 0), 0.55, borrar=3)


def montar(atras, adiante, recorte, contorno=13, com_aba=True, com_fivela=True):
    base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    base = chapa(base, atras, GRAFITE, contorno)
    base = chapa(base, adiante, ACO, contorno)
    if com_aba:
        base = chapa(base, aba(), GRAFITE, contorno)
    if com_fivela:
        base = veu(base, fivela().filter(ImageFilter.GaussianBlur(26)), AMBAR, 0.8)
        pinta(base, engorda(fivela(), 10), CONTORNO)
        pinta(base, fivela(), FOGO)

    # Reflexo diagonal largo, cortado pelas duas chapas.
    lustro = mascara()
    ImageDraw.Draw(lustro).polygon([(0, 470), (S, 120), (S, 300), (0, 650)], fill=255)
    base = veu(base, dentro(lustro, uniao(atras, adiante)), (0xFF, 0xFF, 0xFF), 0.16, borrar=40)

    # Boca do recorte: sombra projetada para dentro do metal, depois a luz que escapa.
    sombra_boca = dentro(menos(engorda(recorte, 26).filter(ImageFilter.GaussianBlur(16)), recorte), adiante)
    base = veu(base, sombra_boca, (0, 0, 0), 0.75)
    base = veu(base, dentro(recorte.filter(ImageFilter.GaussianBlur(34)), adiante), AMBAR, 0.9)
    pinta(base, recorte, FOGO)
    base = veu(base, recorte.filter(ImageFilter.GaussianBlur(9)), (0xFF, 0xF0, 0xC8), 0.35)
    return base


def placa(arte, escala=0.82):
    """Variante com moldura, para comparar com a silhueta recortada."""
    base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    m = rrect(mascara(), (16, 16, S - 16, S - 16), 228)
    pinta(base, m, grad((0, 0), (S, S), [
        (0.00, (0x2E, 0x34, 0x42)), (0.45, (0x1A, 0x1E, 0x28)), (1.00, (0x0B, 0x0D, 0x13))]))
    base = veu(base, dentro(menos(m, move(m, 0, 8)), m), (0xFF, 0xFF, 0xFF), 0.30)
    lado = int(S * escala)
    base.alpha_composite(reduz(arte, lado), ((S - lado) // 2, (S - lado) // 2))
    return base


SIMBOLOS = {
    "play": (lambda: play((512, 742), 232), lambda: play((512, 748), 300)),
    "raio": (lambda: raio((512, 744), 248), lambda: raio((512, 750), 310)),
    "dpad": (lambda: cruz((512, 632), 316, 112, 24), lambda: cruz((512, 600), 392, 150, 30)),
}


def variante(simbolo, com_placa=False):
    g, p = SIMBOLOS[simbolo]
    # Nos tamanhos pequenos: sem pega, recorte maior, contorno mais grosso. Reduzir a
    # arte cheia neles apaga a pega e afina o contorno ate sumir.
    grande = lambda: montar(costas(), frente(), g())
    curto = lambda: montar(costas(pega=False), frente(), p(), contorno=26, com_fivela=False)
    if com_placa:
        return (lambda: placa(grande()), lambda: placa(curto(), escala=0.88))
    return (grande, curto)


VARIANTES = {
    "d-play": variante("play"),
    "e-raio": variante("raio"),
    "f-dpad": variante("dpad"),
    "g-play-placa": variante("play", com_placa=True),
}


def main():
    os.makedirs(SAIDA, exist_ok=True)
    for nome in (sys.argv[1:] or list(VARIANTES)):
        faz_grande, faz_pequeno = VARIANTES[nome]
        grande, curto = faz_grande(), faz_pequeno()
        for lado in (512, 256, 128, 64, 48):
            reduz(grande, lado).save(os.path.join(SAIDA, f"{nome}-{lado}.png"))
        for lado in (32, 24, 16):
            reduz(curto, lado).save(os.path.join(SAIDA, f"{nome}-{lado}.png"))
        print(f"{nome}: ok")


if __name__ == "__main__":
    main()
