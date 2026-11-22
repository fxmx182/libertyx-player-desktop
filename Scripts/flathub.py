#!/usr/bin/env python3
"""
Preenche o manifesto do Flathub com a release do Linux mais nova (ou a tag dada): a tag e o commit
do git, os endereços e os sha256 dos dois .tar.gz e do versao.txt. O resultado é o arquivo que vai
para o repositório do app no Flathub (github.com/flathub/io.github.fxmx182.libertyx-player-desktop).

Uso: python3 Scripts/flathub.py [tag] [saída]
     (sem tag: a release "v…" mais nova; saída padrão: o próprio manifesto em packaging/flatpak)
"""
import hashlib, json, os, re, sys, urllib.request

REPO = "fxmx182/libertyx-player-desktop"
ID = "io.github.fxmx182.libertyx-player-desktop"
AQUI = os.path.dirname(os.path.abspath(__file__))
MANIFESTO = os.path.join(AQUI, "..", "packaging", "flatpak", ID + ".yml")


def baixar(url):
    req = urllib.request.Request(url, headers={"User-Agent": "libertyx-flathub-script"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def main():
    tag = sys.argv[1] if len(sys.argv) > 1 else None
    saida = sys.argv[2] if len(sys.argv) > 2 else MANIFESTO
    if tag is None:
        # o atom das releases não gasta a cota da API (que sem login é pequena)
        atom = baixar(f"https://github.com/{REPO}/releases.atom").decode()
        tags = re.findall(r"releases/tag/(v[\d.]+-\d+-[0-9a-f]+)", atom)
        if not tags:
            sys.exit("nenhuma release do Linux encontrada")
        tag = tags[0]
    base = f"https://github.com/{REPO}/releases/download/{tag}"
    versao = baixar(f"{base}/versao.txt")
    info = dict(l.split("=", 1) for l in versao.decode().splitlines() if "=" in l)
    commit = info["commit"]
    if "data" not in info:
        sys.exit(f"o versao.txt de {tag} não tem a data (a ficha do Flathub precisa): use uma release mais nova")
    somas = {"versao.txt": hashlib.sha256(versao).hexdigest()}
    for nome in ("LibertyXPlayer-linux.tar.gz", "LibertyXPlayer-linux-arm64.tar.gz"):
        print("baixando", nome, "…", file=sys.stderr)
        somas[nome] = hashlib.sha256(baixar(f"{base}/{nome}")).hexdigest()

    m = open(MANIFESTO, encoding="utf-8").read()
    m = re.sub(r"(url: https://github.com/fxmx182/libertyx-player-desktop.git\n\s+tag: )\S+", r"\g<1>" + tag, m)
    m = re.sub(r"(\n\s+tag: " + re.escape(tag) + r"\n\s+commit: )'?[0-9a-f]+'?", r"\g<1>" + commit, m)
    for nome, soma in somas.items():
        m = re.sub(r"(url: https://github.com/fxmx182/libertyx-player-desktop/releases/download/)[^/]+(/" + re.escape(nome) + r"\n\s+sha256: )'?[0-9a-f]+'?",
                   r"\g<1>" + tag + r"\g<2>" + soma, m)
    open(saida, "w", encoding="utf-8").write(m)
    print(f"{saida}: {tag} (versão {info['versao']}, commit {commit[:7]})")


if __name__ == "__main__":
    main()
