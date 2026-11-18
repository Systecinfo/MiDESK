#!/usr/bin/env python3
"""Aplica a marca MiDESK e o servidor próprio no código do RustDesk antes do build.

Roda no GitHub Actions logo após o checkout (com submódulos). Variáveis de ambiente:
  MIDESK_APP_NAME  nome do app (padrão: MiDESK)
  MIDESK_SERVER    IP ou domínio do seu servidor hbbs (opcional; sem ele usa o público)
  MIDESK_KEY       chave pública do servidor (conteúdo do id_ed25519.pub; opcional)

Falha de propósito se algum trecho esperado não for encontrado, para que uma mudança
no upstream não gere um build silenciosamente sem a marca.
"""
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = os.environ.get("MIDESK_APP_NAME", "").strip() or "MiDESK"
SERVER = os.environ.get("MIDESK_SERVER", "").strip()
KEY = os.environ.get("MIDESK_KEY", "").strip()


def patch(rel, pairs):
    path = ROOT / rel
    text = path.read_text(encoding="utf-8")
    for pattern, repl in pairs:
        new, n = re.subn(pattern, repl, text, flags=re.M)
        if n == 0:
            sys.exit(f"[brand] padrão não encontrado em {rel}: {pattern}")
        text = new
    path.write_text(text, encoding="utf-8")
    print(f"[brand] {rel}: ok")


def q(s):
    return s.replace("\\", "\\\\").replace('"', '\\"')


config = [
    (r'(pub static ref APP_NAME: RwLock<String> = RwLock::new\(")RustDesk("\.to_owned\(\)\);)',
     rf'\g<1>{q(APP)}\g<2>'),
]
if SERVER:
    if not re.fullmatch(r"[A-Za-z0-9.\-:\[\]]+", SERVER):
        sys.exit("[brand] MIDESK_SERVER inválido (use só IP ou domínio, ex.: 203.0.113.10 ou remoto.exemplo.com)")
    config.append((r'(pub const RENDEZVOUS_SERVERS: &\[&str\] = &\[)"[^"]*"(\];)', rf'\g<1>"{SERVER}"\g<2>'))
    # Servidor "de fábrica": o app deixa de se achar no servidor público da RustDesk
    # (some a dica "configure seu próprio servidor" que leva à página de preços deles)
    # e a API de conta deixa de cair em admin.rustdesk.com.
    config.append((r'(pub static ref PROD_RENDEZVOUS_SERVER: RwLock<String> = RwLock::new\()""(\.to_owned\(\)\);)',
                   rf'\g<1>"{SERVER}"\g<2>'))
if KEY:
    if not re.fullmatch(r"[A-Za-z0-9+/=]{40,64}", KEY):
        sys.exit("[brand] MIDESK_KEY inválida (cole o conteúdo do id_ed25519.pub)")
    config.append((r'(pub const RS_PUB_KEY: &str = )"[^"]*"(;)', rf'\g<1>"{KEY}"\g<2>'))
# Conta/login, catálogo de endereços e grupos dependem do RustDesk Server Pro (pago);
# com o servidor gratuito eles não funcionam, então ficam ocultos.
config.append((r'(pub fn is_disable_account\(\) -> bool \{\s*)is_some_hard_opton\("disable-account"\)',
               r'\g<1>true'))
patch("libs/hbb_common/src/config.rs", config)

REPO_URL = os.environ.get("MIDESK_REPO_URL", "").strip() or "https://github.com/Systecinfo/MiDESK"
COPYRIGHT = f"Copyright © 2026 {APP}. Baseado no RustDesk (AGPL-3.0)."

cargo_meta = [
    (r'^(description = )"RustDesk Remote Desktop"', rf'\g<1>"{q(APP)} Remote Desktop"'),
    (r'^(ProductName = )"RustDesk"', rf'\g<1>"{q(APP)}"'),
    (r'^(FileDescription = )"RustDesk Remote Desktop"', rf'\g<1>"{q(APP)} Remote Desktop"'),
    (r'^(OriginalFilename = )"rustdesk\.exe"', rf'\g<1>"{q(APP)}.exe"'),
    (r'^(LegalCopyright = )"[^"]*"', rf'\g<1>"{q(COPYRIGHT)}"'),
]
patch("Cargo.toml", cargo_meta)
patch("libs/portable/Cargo.toml", cargo_meta)

patch("flutter/windows/runner/Runner.rc", [
    (r'(VALUE "CompanyName", )"[^"]*"', rf'\g<1>"{q(APP)}"'),
    (r'(VALUE "FileDescription", )"RustDesk Remote Desktop"', rf'\g<1>"{q(APP)} Remote Desktop"'),
    (r'(VALUE "InternalName", )"rustdesk"', rf'\g<1>"{q(APP)}"'),
    (r'(VALUE "LegalCopyright", )"[^"]*"', rf'\g<1>"{q(COPYRIGHT)}"'),
    (r'(VALUE "OriginalFilename", )"rustdesk\.exe"', rf'\g<1>"{q(APP)}.exe"'),
    (r'(VALUE "ProductName", )"RustDesk"', rf'\g<1>"{q(APP)}"'),
])

# Textos fixos da interface (as traduções já trocam "RustDesk" pelo nome do app sozinhas).
patch("flutter/lib/desktop/widgets/tabbar_widget.dart", [
    (r'(child: const Text\(\s*)"RustDesk"', rf'\g<1>"{q(APP)}"'),
])
patch("flutter/lib/common.dart", [
    # esconde o "Powered by RustDesk" da barra lateral (o crédito fica na tela Sobre)
    (r'if \(bind\.mainGetBuildinOption\(key: "hide-powered-by-me"\) == \'Y\'\) \{',
     "if (true) {"),
])
patch("flutter/lib/desktop/pages/desktop_setting_page.dart", [
    (r"launchUrlString\('https://rustdesk\.com/privacy\.html'\)", f"launchUrlString('{REPO_URL}')"),
    (r"launchUrlString\('https://rustdesk\.com'\)", f"launchUrlString('{REPO_URL}')"),
    # aviso legal exigido pela AGPL: mantém o crédito ao RustDesk na tela Sobre
    (r"'Copyright © \$\{DateTime\.now\(\)\.toString\(\)\.substring\(0, 4\)\} Purslane Tech Pte\. Ltd\.\\n\$license'",
     f"'Copyright © ${{DateTime.now().toString().substring(0, 4)}} {APP}\\\\n"
     f"Baseado no RustDesk, Copyright © Purslane Tech Pte. Ltd. (AGPL-3.0)\\\\n$license'"),
])

patch("flutter/lib/desktop/pages/connection_page.dart", [
    (r'const url = "https://rustdesk\.com/pricing";', f'const url = "{REPO_URL}";'),
])
patch("flutter/lib/desktop/pages/install_page.dart", [
    (r"'https://rustdesk\.com/privacy\.html'", f"'{REPO_URL}'"),
])

# Quando a conexão direta (P2P) abre e cai, o RustDesk pergunta se quer usar o relay.
# O MiDESK reconecta pelo relay sozinho (esse aviso só vem de conexão direta, então não entra em loop).
patch("flutter/lib/models/model.dart", [
    (r"(\} else if \(type == 'relay-hint' \|\| type == 'relay-hint2'\) \{\s*)"
     r"showRelayHintDialog\(sessionId, type, title, text, dialogManager, peerId\);",
     r"\g<1>reconnect(dialogManager, sessionId, true);"),
])

print(f"[brand] app={APP} servidor={'próprio: ' + SERVER if SERVER else 'público (padrão)'} "
      f"chave={'própria' if KEY else 'padrão'}")
