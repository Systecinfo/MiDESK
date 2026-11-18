# MiDESK

Acesso remoto baseado no [RustDesk](https://github.com/rustdesk/rustdesk) (AGPL-3.0), com a marca MiDESK,
servidor próprio embutido e um instalador online pequeno.

## Como funciona

```
MiDESK-Setup.exe (~140 KB)
   └─ consulta  api.github.com/repos/Systecinfo/MiDESK/releases/latest
   └─ baixa     MiDESK-<versão>-x86_64.exe   (~25 MB, da Release)
   └─ confere   SHA256SUMS.txt
   └─ instala   --silent-install  ->  C:\Program Files\MiDESK\MiDESK.exe
```

O programa completo é compilado pelo GitHub Actions (`.github/workflows/midesk-windows.yml`) e publicado
nas Releases deste repositório. O instalador pequeno sempre pega a Release mais recente, então
atualizar o MiDESK é só gerar uma nova Release.

## Arquivos do MiDESK neste repositório

| Caminho | O que é |
|---|---|
| `midesk/brand.py` | Troca o nome para MiDESK e embute o servidor/chave no código antes do build |
| `installer/MiDeskSetup.cs` + `installer/build.ps1` | O instalador pequeno (C#, .NET Framework 4 que já vem no Windows) |
| `.github/workflows/midesk-windows.yml` | Build do Windows x64 + instalador + Release |
| `midesk/server/docker-compose.yml` / `setup.sh` | Servidor próprio (hbbs + hbbr) para a VPS |
| `midesk/branding/midesk-icon.svg` | Logo do MiDESK (planeta + circuito + satélite). `make-icons.ps1` gera todos os ícones a partir dele |

O resto é o código do RustDesk sem alterações, para facilitar puxar atualizações do upstream.

## Gerar uma versão

1. (Uma vez) Configure o servidor — veja abaixo — e crie em
   **Settings → Secrets and variables → Actions → Variables**:
   - `MIDESK_SERVER` = IP ou domínio da VPS (ex.: `203.0.113.10`)
   - `MIDESK_KEY` = conteúdo do arquivo `data/id_ed25519.pub` da VPS
2. **Actions → MiDESK Windows → Run workflow**. Leva ~40–60 min na primeira vez (depois o cache acelera).
3. Na Release criada, compartilhe o link do `MiDESK-Setup.exe`.

Sem as variáveis, o MiDESK usa os servidores públicos do RustDesk.

## Precisa hospedar?

**Não é obrigatório, mas é recomendado.** O RustDesk tenta primeiro uma conexão direta entre os dois PCs
(*hole punching*). O servidor serve para:

- **hbbs (rendezvous/ID)** — registra o ID de cada PC e apresenta um ao outro. Sem ele não há como se encontrar.
- **hbbr (relay)** — quando a conexão direta falha (NAT simétrico, CGNAT de operadora 4G/fibra, firewall
  corporativo), todo o vídeo passa por ele.

Os servidores públicos gratuitos são compartilhados com o mundo todo, ficam fora do Brasil e costumam ser
lentos no relay. Uma VPS própria perto dos usuários dá latência menor, banda dedicada, e privacidade
(a chave é sua, ninguém sem ela usa seu servidor).

### Requisitos da VPS

- 1 vCPU, 1 GB RAM é suficiente para dezenas de conexões; o gargalo é a **banda** do relay
  (~1–3 Mbps por sessão relayada em 1080p).
- Linux com Docker. Prefira um datacenter no Brasil (São Paulo) se os usuários estão aqui.
- IP fixo ou domínio.

### Portas a liberar no firewall da VPS

| Porta | Protocolo | Serviço |
|---|---|---|
| 21115 | TCP | hbbs (teste de NAT) |
| 21116 | TCP **e UDP** | hbbs (ID / hole punching) |
| 21117 | TCP | hbbr (relay) |
| 21118, 21119 | TCP | WebSocket (só se usar o cliente web) |

### Subir o servidor

```bash
mkdir -p ~/midesk-server && cd ~/midesk-server
curl -fsSLO https://raw.githubusercontent.com/Systecinfo/MiDESK/master/midesk/server/docker-compose.yml
docker compose up -d
cat data/id_ed25519.pub      # -> valor de MIDESK_KEY
```

Com a chave configurada, o hbbs só aceita clientes que tenham essa chave — clientes MiDESK, no caso.

## Licença

AGPL-3.0, igual ao RustDesk: o código-fonte (incluindo suas alterações) precisa continuar público para quem
usa o programa — por isso este repositório é público. "RustDesk" e o logo são marcas do projeto original;
por isso o MiDESK usa nome e logo próprios, e mantém o crédito ao RustDesk na tela Sobre (aviso legal exigido pela AGPL).
