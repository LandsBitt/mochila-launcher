"""Transforma a arte crua do gerador no PNG que vai embutido no Mochila.exe.

    python assets\\preparar-icone.py assets\\mochila-fonte.png assets\\mochila.png 256

Roda em tempo de desenvolvimento, quando a arte muda. O executavel nao depende disso:
ele carrega o mochila.png ja pronto do proprio recurso. Precisa de Pillow e numpy.

O que o script faz, em ordem: apaga o fundo branco sem furar o corpo creme da mochila,
recorta a arte no proprio contorno, gruda o ruido do gerador nas cores canonicas e
reduz para o tamanho pedido com o alfa pre-multiplicado.
"""
import sys
from collections import deque

import numpy as np
from PIL import Image

ORIGEM = sys.argv[1]
DESTINO = sys.argv[2]
LADO = int(sys.argv[3]) if len(sys.argv) > 3 else 256

img = Image.open(ORIGEM).convert("RGBA")
a = np.array(img).astype(np.int16)
h, w = a.shape[:2]

# "Quase branco": o fundo do gerador nao e 255 puro em todo pixel.
claro = (a[:, :, 0] > 235) & (a[:, :, 1] > 235) & (a[:, :, 2] > 235)

# Flood fill a partir das bordas. Nao serve marcar "todo branco": o corpo da mochila
# e os botoes sao creme quase branco e virariam buraco.
fora = np.zeros((h, w), dtype=bool)
fila = deque()
for x in range(w):
    for y in (0, h - 1):
        if claro[y, x] and not fora[y, x]:
            fora[y, x] = True
            fila.append((y, x))
for y in range(h):
    for x in (0, w - 1):
        if claro[y, x] and not fora[y, x]:
            fora[y, x] = True
            fila.append((y, x))

while fila:
    y, x = fila.popleft()
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        ny, nx = y + dy, x + dx
        if 0 <= ny < h and 0 <= nx < w and claro[ny, nx] and not fora[ny, nx]:
            fora[ny, nx] = True
            fila.append((ny, nx))

alpha = np.where(fora, 0, 255).astype(np.uint8)

# Borda antialiasada: o pixel logo fora do contorno e um cinza entre branco e preto.
# Sem essa faixa o recorte fica serrilhado depois da reducao.
vizinho_dentro = np.zeros((h, w), dtype=bool)
vizinho_dentro[1:, :] |= ~fora[:-1, :]
vizinho_dentro[:-1, :] |= ~fora[1:, :]
vizinho_dentro[:, 1:] |= ~fora[:, :-1]
vizinho_dentro[:, :-1] |= ~fora[:, 1:]
borda = fora & vizinho_dentro
luz = a[:, :, :3].max(axis=2)
alpha[borda] = np.clip(255 - luz[borda], 0, 255).astype(np.uint8)

# O gerador deixou ~5000 tons onde a arte tem 3 cores: cada area chapada oscila +-2.
# Grudar esse ruido nas cores canonicas limpa o desenho e derruba o PNG de 68 para 40 KB.
# A tolerancia e curta de proposito: a transicao real entre duas cores (o antialias do
# contorno) fica longe de todas elas e passa intacta.
CANONICAS = np.array([[110, 99, 229],    # roxo da mochila
                      [244, 240, 229],   # creme do corpo
                      [20, 23, 32]],     # tinta do contorno
                     dtype=np.int16)
rgb = a[:, :, :3]
for cor in CANONICAS:
    perto = np.abs(rgb - cor).max(axis=2) <= 12
    rgb[perto] = cor

rgba = np.dstack([rgb.astype(np.uint8), alpha])
recortada = Image.fromarray(rgba)

caixa = recortada.getchannel("A").getbbox()
recortada = recortada.crop(caixa)

# Quadrado com 3% de folga: icone colado na moldura fica apertado na barra de tarefas.
lado_conteudo = max(recortada.size)
lado_total = int(round(lado_conteudo / 0.94))
tela = Image.new("RGBA", (lado_total, lado_total), (0, 0, 0, 0))
tela.paste(recortada, ((lado_total - recortada.width) // 2, (lado_total - recortada.height) // 2))

# Reducao com alfa pre-multiplicado: sem isso o branco do fundo transparente
# vaza para dentro do contorno e sobra um halo claro.
f = np.array(tela).astype(np.float64)
f[:, :, :3] *= f[:, :, 3:4] / 255.0
pequena = np.array(Image.fromarray(f.astype(np.uint8)).resize((LADO, LADO), Image.LANCZOS)).astype(np.float64)
al = np.clip(pequena[:, :, 3:4], 1, 255)
pequena[:, :, :3] = np.clip(pequena[:, :, :3] * 255.0 / al, 0, 255)
final = Image.fromarray(pequena.astype(np.uint8))
final.save(DESTINO, "PNG", optimize=True)

print(f"{DESTINO}: {final.size[0]}x{final.size[1]}, conteudo original {caixa}")
