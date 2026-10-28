#!/usr/bin/env python3
"""
Confere as traduções: todo texto passado a S(), F() e P() no código precisa estar nas tabelas de
inglês e espanhol (src/LibertyX.Player/Core/Textos.Tabelas.cs). Lista o que falta e o que sobra.

Uso: python3 Scripts/textos.py
"""
import glob, os, re, sys

RAIZ = os.path.join(os.path.dirname(os.path.abspath(__file__)), "../src/LibertyX.Player")
LIT = r'"((?:[^"\\]|\\.)*)"'
usos = re.compile(r'\b(?:S|F|Textos\.S|Textos\.F|Core\.Textos\.S)\(\s*' + LIT)
plurais = re.compile(r'\bP\([^,]+,\s*' + LIT + r'\s*,\s*' + LIT)

codigo = []
for f in sorted(glob.glob(os.path.join(RAIZ, "**/*.cs"), recursive=True)):
    if "/obj/" in f or "/bin/" in f or f.endswith("Textos.Tabelas.cs"):
        continue
    t = open(f, encoding="utf-8").read()
    codigo += [m.group(1) for m in usos.finditer(t)]
    for m in plurais.finditer(t):
        codigo += [m.group(1), m.group(2)]
codigo = list(dict.fromkeys(codigo))

tab = open(os.path.join(RAIZ, "Core/Textos.Tabelas.cs"), encoding="utf-8").read()
en_ini, es_ini = tab.index("En = new()"), tab.index("Es = new()")
chave = re.compile(r'\[' + LIT + r'\]\s*=')
en = {m.group(1) for m in chave.finditer(tab[en_ini:es_ini])}
es = {m.group(1) for m in chave.finditer(tab[es_ini:])}

falta_es = [c for c in codigo if c not in es]
sobra = [c for c in es if c not in codigo]
for c in falta_es: print("falta (es):", c)
for c in sobra: print("sobra:", c)
print(f"{len(codigo)} textos no código; inglês com {len(en)} diferentes do português; espanhol com {len(es)}")
sys.exit(1 if falta_es else 0)
