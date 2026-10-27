from pathlib import Path
import threading,functools,http.server,os,shutil
from playwright.sync_api import sync_playwright
repository=Path(__file__).resolve().parents[1]
root=repository/'frontend/dist/viteka-ui/browser'
artifacts=repository/'artifacts/ui';artifacts.mkdir(parents=True,exist_ok=True)
class Quiet(http.server.SimpleHTTPRequestHandler):
 def log_message(self,*args):pass
server=http.server.ThreadingHTTPServer(('127.0.0.1',0),functools.partial(Quiet,directory=str(root)))
threading.Thread(target=server.serve_forever,daemon=True).start()
url=f'http://127.0.0.1:{server.server_port}'
with sync_playwright() as p:
 executable=os.environ.get('VITEKA_CHROME') or shutil.which('chromium') or shutil.which('google-chrome') or str(Path(os.environ.get('PROGRAMFILES','C:/Program Files'))/'Google/Chrome/Application/chrome.exe')
 browser=p.chromium.launch(executable_path=executable,headless=True)
 page=browser.new_page(viewport={'width':1240,'height':772});errors=[];page.on('pageerror',lambda e:errors.append(str(e)));page.on('console',lambda e: errors.append(e.text) if e.type=='error' else None)
 page.route('**/favicon.ico',lambda route:route.fulfill(status=204));page.goto(url);page.get_by_role('heading',name='Tu farmacia, más ágil').wait_for()
 assert page.locator('aside').bounding_box()['width']==218
 assert page.locator('.empty .icon').bounding_box()['width']==76
 page.screenshot(path=str(artifacts/'angular-desktop.png'),full_page=True)
 assert page.get_by_role('button',name='Inicio',exact=True).get_attribute('aria-current')=='page'
 for name,message in [('Utilidades','Aún no hay utilidades disponibles'),('Favoritos','Aún no tienes favoritos'),('Actividad','Todavía no hay actividad')]:
  page.get_by_role('navigation').get_by_role('button',name=name,exact=True).click();page.get_by_role('heading',name=message,exact=True).wait_for()
 assert page.get_by_text('Analizar stock',exact=True).count()==0
 page.get_by_role('button',name='Configuración',exact=True).click();assert page.get_by_role('button',name='Buscar actualizaciones',exact=True).is_disabled()
 page.get_by_role('checkbox').check();page.reload();page.get_by_role('heading',name='Aún no tienes favoritos').wait_for()
 page.get_by_role('navigation').get_by_role('button',name='Inicio',exact=True).click()
 page.set_viewport_size({'width':960,'height':680});assert page.evaluate('document.documentElement.scrollWidth<=innerWidth')
 page.close()
 native=browser.new_page(viewport={'width':1240,'height':772})
 native.add_init_script('''window.testCalls=[];window.chrome=window.chrome||{};let listener;window.chrome.webview={addEventListener:(name,fn)=>listener=fn,postMessage:msg=>{window.testCalls.push(msg.command);setTimeout(()=>{let result={};if(msg.command==='app.state')result={version:'0.7.0',openFavorites:false};if(msg.command==='update.check')result={available:true,version:'0.8.0'};if(msg.command==='update.install')result={cancelled:true};listener({data:{id:msg.id,ok:true,result}});},5);}};''')
 native.on('pageerror',lambda e:errors.append(str(e)))
 native.goto(url);native.get_by_role('heading',name='Tu farmacia, más ágil').wait_for();assert native.get_by_text('Vista previa en navegador',exact=False).count()==0
 native.get_by_role('button',name='Configuración',exact=True).click();native.get_by_role('button',name='Buscar actualizaciones',exact=True).click()
 native.get_by_text('Versión 0.8.0 disponible.',exact=True).wait_for();native.get_by_role('button',name='Descargar e instalar',exact=True).click()
 native.get_by_text('Instalación cancelada. Puedes seguir usando esta versión.',exact=True).wait_for()
 assert native.evaluate('window.testCalls')==['app.state','update.check','update.install']
 assert not errors,errors
 browser.close()
server.shutdown()
print('PASS: 5 sections, empty catalog, preferences after reload, browser update disabled, native message flow, cancelled update, 960px layout, no JS errors.')
