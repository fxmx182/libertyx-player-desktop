# LibertyX Player na Snap Store

A Snap Store é a loja que o Ubuntu mostra na App Center: publicado lá, o LibertyX aparece na busca
de quem usa Ubuntu (e de quem tem o snapd em outra distribuição). Página: https://snapcraft.io/libertyx-player

## Como sai uma versão

1. Um commit `Linux:` publica a release do Linux (workflow `linux.yml`).
2. Ao terminar, o `snap.yml`, no x86_64 e no ARM64 (cada um no próprio runner):
   - escreve em `tag.txt` qual é a release mais nova;
   - empacota o snap (`snapcraft pack --destructive-mode`, direto no Ubuntu 24.04 do runner): o `snapcraft.yaml` baixa o AppImage daquela
     release e tira dele o executável, o libmpv LGPL, os ícones e o atalho;
   - publica no canal `stable` com a chave do segredo `SNAPCRAFT_STORE_CREDENTIALS`.
3. Quem tem o snap recebe a atualização sozinho.

Sem o segredo, o workflow compila e guarda o `.snap` como artefato, mas não publica.

## Na caixinha

- `base: core24` com a extensão `gnome` (OpenGL, fontes, som, temas e o GnuTLS que o libmpv usa).
- Acessos: `home` (a pasta pessoal, sem os arquivos ocultos), `removable-media` (pendrives e
  outros discos: o usuário libera com `snap connect libertyx-player:removable-media`, ou a loja
  libera para todos depois de um pedido no fórum), rede, som e "não apagar a tela".
- O `$HOME` do snap é `~/snap/libertyx-player/N`: config e cache ficam lá. Os vídeos vêm da pasta
  pessoal de verdade (`$SNAP_REAL_HOME`, ver `Caminhos.Home`).
- Dentro do snap o app não se atualiza sozinho e a pergunta do player padrão só explica onde
  escolher (a caixinha não deixa gravar no `mimeapps.list`).

## Testar aqui

Num Ubuntu 24.04 (máquina virtual) com `sudo snap install snapcraft --classic`:

```
cd packaging/snap
sudo snapcraft pack --destructive-mode
sudo snap install --dangerous libertyx-player_*.snap
xvfb-run -a snap run libertyx-player --autoteste ~/r.txt ~/Videos/teste.mp4
```
