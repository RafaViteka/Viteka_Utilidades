from pathlib import Path
import shutil
root = Path(__file__).resolve().parents[1]
source = root / 'frontend/dist/viteka-ui/browser'
if not (source / 'index.html').is_file():
    raise SystemExit('Build the Angular production UI before copying it.')
destination = root / 'src/Viteka.Desktop/Web'
if destination.exists():
    shutil.rmtree(destination)
shutil.copytree(source, destination)
print('Angular UI copied into the desktop package.')
