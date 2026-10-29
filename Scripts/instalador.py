#!/usr/bin/env python3
"""
O que o Windows pede além do app: o ícone do .exe (packaging/windows/app.ico, 16 a 256) e as artes
do instalador (Inno Setup) — a faixa da esquerda das telas de boas-vindas e de conclusão, e o selo do
canto das outras. A mesma linguagem do instalador do Partition (preto quente, brilho dourado vindo do
alto, o símbolo em ouro), com o motivo do player no rodapé: o "play" e a barra de tempo.

Uso: toolchain/icon-venv/bin/python Scripts/instalador.py
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.join(AQUI, "..")
ASSETS = os.path.join(RAIZ, "src/LibertyX.Player/Assets")
ICONES = os.path.join(RAIZ, "packaging/linux/icons")
SAIDA = os.path.join(RAIZ, "packaging/windows")
OURO = (251, 197, 1)
FUNDO = (12, 11, 10)


def fonte(peso, tam):
    return ImageFont.truetype(os.path.join(ASSETS, f"Fonts/manrope_{peso}.ttf"), tam)


def faixa(esc):
    w, h = 164 * esc, 314 * esc
    im = Image.new("RGB", (w, h), FUNDO)
    luz = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(luz)
    r = int(w * 1.05)
    d.ellipse((-r // 2, -r // 2 - 20 * esc, r // 2 + 10 * esc, r // 2 - 20 * esc), fill=60)
    luz = luz.filter(ImageFilter.GaussianBlur(40 * esc))
    im.paste(Image.new("RGB", (w, h), OURO), (0, 0), luz)

    # o rodapé: um "play" grande em traço fino, cortado pela borda, e a barra de tempo com o ponto
    arte = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(arte)
    for i, k in enumerate((1.0, 0.82, 0.64)):
        x0, y0 = int(w * 0.30), int(h * 0.60)
        lado = int(150 * esc * k)
        cy = y0 + int(150 * esc) // 2 + int(40 * esc)
        pts = [(x0 + int(20 * esc * (1 - k) * 3), cy - lado // 2), (x0 + int(20 * esc * (1 - k) * 3), cy + lado // 2),
               (x0 + int(20 * esc * (1 - k) * 3) + int(lado * 0.87), cy)]
        d.line(pts + [pts[0]], fill=OURO + (110 - i * 35,), width=max(1, esc), joint="curve")
    yb = h - 26 * esc
    d.line((16 * esc, yb, w - 16 * esc, yb), fill=(255, 255, 255, 40), width=3 * esc)
    d.line((16 * esc, yb, int(w * 0.62), yb), fill=OURO + (235,), width=3 * esc)
    pr = 5 * esc
    d.ellipse((int(w * 0.62) - pr, yb - pr, int(w * 0.62) + pr, yb + pr), fill=OURO + (255,))
    im.paste(arte, (0, 0), arte)

    logo = Image.open(os.path.join(ASSETS, "marca.png")).convert("RGBA")
    lado = 58 * esc
    logo = logo.resize((lado, lado), Image.LANCZOS)
    x0, y0 = 18 * esc, 30 * esc
    m = 30 * esc
    forma = Image.new("L", (lado + 2 * m, lado + 2 * m), 0)
    forma.paste(logo.split()[3], (m, m))
    brilho = forma.filter(ImageFilter.GaussianBlur(10 * esc)).point(lambda v: int(v * 0.5))
    im.paste(Image.new("RGB", forma.size, OURO), (x0 - m, y0 - m), brilho)
    im.paste(logo, (x0, y0), logo)
    d = ImageDraw.Draw(im)
    y = y0 + lado + 16 * esc
    f = fonte("bold", 9 * esc)
    xx = x0 + 1 * esc
    for ch in "LIBERTYX":
        d.text((xx, y), ch, font=f, fill=OURO)
        xx += f.getlength(ch) + 2.6 * esc
    d.text((x0, y + 11 * esc), "Player", font=fonte("extrabold", 24 * esc), fill=(246, 242, 232))
    d.text((x0 + 1 * esc, y + 44 * esc), "Seus vídeos, do\ncomputador e da rede", font=fonte("medium", 10 * esc),
           fill=(170, 164, 152), spacing=3 * esc)
    return im


def selo(esc):
    lado = 55 * esc
    ic = Image.open(os.path.join(ICONES, "512.png")).convert("RGBA").resize((lado, lado), Image.LANCZOS)
    fundo = Image.new("RGB", (lado, lado), (255, 255, 255))   # o cabeçalho do assistente é claro
    fundo.paste(ic, (0, 0), ic)
    return fundo


os.makedirs(SAIDA, exist_ok=True)
for esc, suf in ((1, ""), (2, "-2x")):
    faixa(esc).save(os.path.join(SAIDA, f"wizard{suf}.bmp"))
    selo(esc).save(os.path.join(SAIDA, f"small{suf}.bmp"))
base = Image.open(os.path.join(ICONES, "512.png")).convert("RGBA")
base.save(os.path.join(SAIDA, "app.ico"), sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
print("ok")
