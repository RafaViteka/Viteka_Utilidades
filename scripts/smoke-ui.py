from pathlib import Path
import threading, functools, http.server, os, shutil, re
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
 page=browser.new_page(viewport={'width':1240,'height':820});errors=[]
 page.on('pageerror',lambda e:errors.append(str(e)));page.on('console',lambda e:errors.append(e.text) if e.type=='error' else None)
 page.route('**/favicon.ico',lambda route:route.fulfill(status=204));page.goto(url)
 page.get_by_role('heading',name='Portal de soporte',exact=True).wait_for()
 assert page.locator('aside').bounding_box()['width']==220
 assert page.locator('.brand-bottom .wordmark').is_visible()
 assert page.locator('aside > img').count()==0
 assert 'MVP visual' not in page.locator('body').inner_text()
 page.screenshot(path=str(artifacts/'angular-desktop.png'),full_page=True)
 page.keyboard.down('Control');page.mouse.wheel(0,-120);page.keyboard.up('Control')
 page.get_by_role('button',name='Restablecer zoom',exact=True).get_by_text('110 %',exact=True).wait_for()
 page.reload();page.get_by_role('button',name='Restablecer zoom',exact=True).get_by_text('110 %',exact=True).wait_for()
 page.get_by_role('button',name='Restablecer zoom',exact=True).click()
 page.get_by_role('button',name='Restablecer zoom',exact=True).get_by_text('100 %',exact=True).wait_for()
 page.mouse.wheel(0,200);assert page.get_by_role('button',name='Restablecer zoom',exact=True).inner_text()=='100 %'
 page.get_by_role('button',name='Cerrar menú lateral',exact=True).click()
 page.wait_for_function("() => Math.round(document.querySelector('aside').getBoundingClientRect().width)===76")
 page.reload();page.get_by_role('button',name='Abrir menú lateral',exact=True).wait_for()
 page.get_by_role('button',name='Abrir menú lateral',exact=True).click()
 page.get_by_label('Apariencia',exact=True).select_option('dark');page.wait_for_function("() => document.documentElement.dataset.theme==='dark'")
 page.reload();page.wait_for_function("() => document.documentElement.dataset.theme==='dark'")
 page.screenshot(path=str(artifacts/'angular-dark.png'),full_page=True)
 page.get_by_label('Apariencia',exact=True).select_option('light')
 page.get_by_role('button',name='¿Cómo solicito ayuda?',exact=True).click();page.get_by_role('dialog').wait_for()
 page.keyboard.press('Escape');page.get_by_role('dialog').wait_for(state='hidden')
 for name,message in [('Utilidades','Aún no hay utilidades disponibles'),('Favoritos','Aún no tienes favoritos'),('Actividad','Todavía no hay actividad')]:
  page.get_by_role('navigation').get_by_role('button',name=name,exact=True).click();page.get_by_role('heading',name=message,exact=True).wait_for()
 assert page.get_by_text('Analizar stock',exact=True).count()==0
 page.get_by_role('button',name='Configuración',exact=True).click();assert page.get_by_role('button',name='Buscar actualizaciones',exact=True).is_disabled()
 page.get_by_role('checkbox').check();page.reload();page.get_by_role('heading',name='Aún no tienes favoritos').wait_for()
 page.get_by_role('navigation').get_by_role('button',name='Inicio',exact=True).click()
 page.set_viewport_size({'width':820,'height':600});assert page.evaluate('document.documentElement.scrollWidth<=innerWidth')
 page.close()
 native=browser.new_page(viewport={'width':1240,'height':820})
 native.add_init_script('''window.testCalls=[];window.chrome=window.chrome||{};let listener;window.chrome.webview={addEventListener:(name,fn)=>listener=fn,postMessage:msg=>{window.testCalls.push(msg);setTimeout(()=>{let result={};if(msg.command==='app.state')result={version:'0.8.0',openFavorites:false,sidebarCollapsed:false,theme:'auto'};if(msg.command==='window.zoom')result={percent:msg.payload.percent};if(msg.command==='update.check')result={available:true,version:'0.9.0'};if(msg.command==='update.install')result={cancelled:true};if(msg.command==='resource.open')result={opened:msg.payload.id!=='anydesk'};listener({data:{id:msg.id,ok:true,result}});},5);}};''')
 native.on('pageerror',lambda e:errors.append(str(e)))
 native.goto(url);native.get_by_role('heading',name='Portal de soporte',exact=True).wait_for()
 native.get_by_role('button',name='Crear o consultar una solicitud',exact=True).click()
 native.wait_for_function("() => window.testCalls.some(x=>x.command==='resource.open'&&x.payload.id==='support')")
 native.get_by_role('button',name='Abrir AnyDesk',exact=True).click();native.get_by_role('dialog').wait_for()
 native.get_by_role('button',name='Cerrar ayuda',exact=True).click()
 native.get_by_role('button',name=re.compile('^Manuales Nixfarma')).click()
 native.get_by_role('button',name='Copiar correo de soporte',exact=True).click();native.get_by_role('status').get_by_text('Correo copiado.',exact=True).wait_for()
 native.get_by_role('button',name='Configuración',exact=True).click();native.get_by_role('button',name='Buscar actualizaciones',exact=True).click()
 native.get_by_text('Versión 0.9.0 disponible.',exact=True).wait_for();native.get_by_role('button',name='Descargar e instalar',exact=True).click()
 native.get_by_text('Instalación cancelada. Puedes seguir usando esta versión.',exact=True).wait_for()
 commands=native.evaluate('window.testCalls')
 assert any(x['command']=='resource.open' and x['payload']['id']=='manuals' for x in commands)
 assert any(x['command']=='clipboard.copy' and x['payload']['id']=='support-email' for x in commands)
 assert not errors,errors
 browser.close()
server.shutdown()
print('PASS: portal, resources, branding, persistent sidebar and themes, help modal, empty utilities, preferences, native resource/clipboard/update messages, 820px layout and no JS errors.')
