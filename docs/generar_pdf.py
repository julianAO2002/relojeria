"""Generate the synchronization case-study report (PDF) from README.md and docs/salidas/*.txt.

Builds an HTML page (README rendered with marked.js, the real outputs and the
main source files), prints it with headless Chrome/Edge and writes
docs/Caso_Sincronizacion_Olivera.pdf.

    python docs/generar_pdf.py
"""

import datetime
import html
import json
import os
import shutil
import subprocess
import sys
import tempfile

DOCS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(DOCS)
PDF = os.path.join(DOCS, "Caso_Sincronizacion_Olivera.pdf")
REPO_URL = "https://github.com/julianAO2002/relojeria"

BROWSERS = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    "/usr/bin/google-chrome",
    "/usr/bin/chromium",
]

OUTPUTS = [
    ("Tests unitarios (dotnet test)", "docs/salidas/tests.txt"),
    ("Ejercicio 1 — Cristian", "docs/salidas/ej1_cristian.txt"),
    ("Ejercicio 2 — Berkeley (7 escenarios)", "docs/salidas/ej2_berkeley.txt"),
    ("Ejercicio 3 — Lamport", "docs/salidas/ej3_lamport.txt"),
]

SOURCES = [
    "src/Clocks/SimulatedClock.cs",
    "src/Clocks/Cristian.cs",
    "src/Clocks/Berkeley.cs",
    "src/Clocks/LogicalClocks.cs",
    "src/Clocks/EventProcess.cs",
    "src/Cristian.TimeServer/Program.cs",
    "src/Cristian.Client/Program.cs",
    "src/Berkeley.Node/Program.cs",
    "src/Berkeley.Coordinator/Program.cs",
    "src/Lamport.Orders/Program.cs",
    "src/Lamport.Orders/Timeline.cs",
    "src/Lamport.Inventory/Program.cs",
    "src/Lamport.Payments/Program.cs",
    "tests/Clocks.Tests/ClockTests.cs",
    "Dockerfile",
    "docker-compose.yml",
]

CSS = """
@page { size: A4; margin: 18mm 16mm 18mm 16mm;
  @bottom-left { content: "Relojería — Caso práctico: Sincronización de relojes en SD"; font: 8pt Arial, sans-serif; color: #666; }
  @bottom-right { content: "Página " counter(page) " de " counter(pages); font: 8pt Arial, sans-serif; color: #666; }
}
@page :first { @bottom-left { content: none; } @bottom-right { content: none; } }
body { font-family: Arial, Helvetica, sans-serif; font-size: 10pt; line-height: 1.45; color: #111; }
h1 { font-size: 17pt; border-bottom: 2px solid #1f4e79; padding-bottom: 4px; color: #1f4e79; margin-top: 18px; }
h2 { font-size: 14pt; color: #1f4e79; margin-top: 18px; }
h3 { font-size: 11.5pt; color: #222; margin-top: 16px; break-after: avoid; }
h1, h2 { break-after: avoid; }
p, li { text-align: justify; }
code { font-family: Consolas, "Courier New", monospace; font-size: 8.8pt; background: #f1f3f5; padding: 0 2px; border-radius: 2px; }
pre { background: #f6f8fa; border: 1px solid #d0d7de; border-radius: 4px; padding: 7px 9px;
      font-size: 7.8pt; line-height: 1.3; white-space: pre-wrap; word-break: break-all; }
pre code { background: none; padding: 0; font-size: inherit; }
table { border-collapse: collapse; width: 100%; margin: 8px 0; font-size: 9pt; break-inside: avoid; }
th, td { border: 1px solid #bbb; padding: 4px 6px; vertical-align: top; text-align: left; }
th { background: #e8eef5; }
blockquote { border-left: 3px solid #1f4e79; margin: 8px 0; padding: 2px 10px; color: #444; background: #f7f9fc; }
a { color: #1f4e79; text-decoration: none; }
.cover { height: 250mm; display: flex; flex-direction: column; justify-content: center; text-align: center; break-after: page; }
.cover .inst { font-size: 12pt; color: #333; }
.cover .title { font-size: 26pt; font-weight: bold; color: #1f4e79; margin: 26px 0 6px; }
.cover .subtitle { font-size: 15pt; color: #333; margin-bottom: 40px; }
.cover table { width: 70%; margin: 0 auto; font-size: 11pt; }
.cover td { border: none; border-bottom: 1px solid #ddd; padding: 6px; }
.annex { break-before: page; }
"""

PAGE = """<!doctype html>
<html lang="es"><head><meta charset="utf-8"><title>Relojería - Sincronización</title>
<style>{css}</style>
<script src="https://cdn.jsdelivr.net/npm/marked@15.0.12/marked.min.js"></script>
</head><body>
<section class="cover">
  <div class="inst">Universidad Autónoma de Entre Ríos — Facultad de Ciencia y Tecnología<br>
  Licenciatura en Sistemas de Información — Sistemas Operativos / Sistemas Distribuidos</div>
  <div class="title">Relojería</div>
  <div class="subtitle">Caso práctico: Sincronización de relojes<br>en Sistemas Distribuidos</div>
  <table>
    <tr><td><b>Alumno</b></td><td>Julián Olivera</td></tr>
    <tr><td><b>Docente</b></td><td>Ing. Mg. Ulises Rapallini</td></tr>
    <tr><td><b>Repositorio</b></td><td><a href="{repo}">{repo}</a></td></tr>
    <tr><td><b>Fecha</b></td><td>{date}</td></tr>
  </table>
</section>
<main id="readme"></main>
<section class="annex">
<h1>Anexo A — Salidas completas de los scripts</h1>
<p>Registros de ejecución generados con los scripts de <code>scripts/</code>. Son las salidas reales a partir de las cuales se respondieron los ejercicios.</p>
{outputs}
</section>
<section class="annex">
<h1>Anexo B — Código fuente principal</h1>
{sources}
</section>
<script>
  const md = {markdown};
  document.getElementById("readme").innerHTML = marked.parse(md);
  document.title = "Relojería - Sincronización";
</script>
</body></html>
"""


def read(rel):
    with open(os.path.join(ROOT, rel), encoding="utf-8") as f:
        return f.read()


def readme_for_pdf():
    text = read("README.md")
    # The cover already has the title and the PDF does not need to link to itself.
    lines = [l for l in text.splitlines() if not l.startswith("El informe en PDF")]
    if lines and lines[0].startswith("# Relojería"):
        lines[0] = "# Descripción del sistema"
    return "\n".join(lines)


def find_browser():
    for path in BROWSERS:
        if os.path.exists(path):
            return path
    for name in ("google-chrome", "chromium", "chrome", "msedge"):
        found = shutil.which(name)
        if found:
            return found
    sys.exit("Chrome or Edge not found")


def main():
    outputs = "".join(
        f"<h2>{html.escape(title)}</h2><pre>{html.escape(read(rel))}</pre>" for title, rel in OUTPUTS
    )
    sources = "".join(f"<h2>{html.escape(rel)}</h2><pre>{html.escape(read(rel))}</pre>" for rel in SOURCES)
    page = PAGE.format(
        css=CSS,
        repo=REPO_URL,
        date=datetime.date.today().strftime("%d/%m/%Y"),
        outputs=outputs,
        sources=sources,
        # Escape "</" so the README cannot close the <script> tag.
        markdown=json.dumps(readme_for_pdf()).replace("</", "<\\/"),
    )

    with tempfile.TemporaryDirectory() as tmp:
        html_path = os.path.join(tmp, "informe.html")
        with open(html_path, "w", encoding="utf-8") as f:
            f.write(page)
        subprocess.run(
            [
                find_browser(),
                "--headless=new",
                "--disable-gpu",
                "--no-pdf-header-footer",
                "--virtual-time-budget=15000",
                f"--user-data-dir={os.path.join(tmp, 'profile')}",
                f"--print-to-pdf={PDF}",
                "file:///" + html_path.replace("\\", "/"),
            ],
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
    print(f"PDF generated: {PDF}")


if __name__ == "__main__":
    main()
