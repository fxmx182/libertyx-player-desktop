#!/usr/bin/env python3
"""
A marca do LibertyX Player para o Linux, a partir do vetor do Android (ic_marca.xml, gerado por
player-android/Scripts/icone.py): o símbolo sem fundo para dentro do app, e o ícone quadrado
(o mesmo da Play) nos tamanhos do hicolor.

Uso: toolchain/icon-venv/bin/python Scripts/marca.py
"""
import io, os, re
import resvg_py
from PIL import Image

AQUI = os.path.dirname(os.path.abspath(__file__))
ANDROID = os.path.join(AQUI, "../../player-android")
XML = open(os.path.join(ANDROID, "app/src/main/res/drawable/ic_marca.xml")).read()
APP = os.path.join(AQUI, "../src/LibertyX.Player/Assets")
ICONES = os.path.join(AQUI, "../packaging/linux/icons")

paths = re.findall(r'<path(.*?)</path>', XML, re.S)
svg = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 46.68 46.68"><defs>']
corpo = []
for i, p in enumerate(paths):
    d = re.search(r'pathData="([^"]+)"', p).group(1)
    regra = "evenodd" if "evenOdd" in p else "nonzero"
    g = re.search(r'startX="([\d.]+)" android:startY="([\d.]+)" android:endX="([\d.]+)" android:endY="([\d.]+)"', p).groups()
    stops = re.findall(r'offset="([\d.]+)" android:color="#FF(\w{6})"', p)
    svg.append(f'<linearGradient id="g{i}" gradientUnits="userSpaceOnUse" x1="{g[0]}" y1="{g[1]}" x2="{g[2]}" y2="{g[3]}">'
               + "".join(f'<stop offset="{o}" stop-color="#{c}"/>' for o, c in stops) + '</linearGradient>')
    corpo.append(f'<path fill-rule="{regra}" fill="url(#g{i})" d="{d}"/>')
svg.append('</defs><g transform="translate(-30.66,-30.66)">' + "".join(corpo) + '</g></svg>')
svg = "".join(svg)

png = bytes(resvg_py.svg_to_bytes(svg_string=svg, width=256, height=256))
open(os.path.join(APP, "marca.png"), "wb").write(png)

os.makedirs(ICONES, exist_ok=True)
base = Image.open(os.path.join(ANDROID, "Scripts/play-512.png")).convert("RGBA")
# cantos arredondados como os ícones de app do GNOME/KDE (o da Play é quadrado)
from PIL import ImageDraw
mask = Image.new("L", base.size, 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, 511, 511], radius=112, fill=255)
base.putalpha(mask)
for s in (16, 24, 32, 48, 64, 128, 256, 512):
    base.resize((s, s), Image.LANCZOS).save(os.path.join(ICONES, f"{s}.png"))
base.resize((256, 256), Image.LANCZOS).save(os.path.join(APP, "icone-256.png"))
print("ok")
