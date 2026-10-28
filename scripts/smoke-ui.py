from pathlib import Path
import threading,functools,http.server,os,shutil,re,copy
from playwright.sync_api import sync_playwright,expect
repository=Path(__file__).resolve().parents[1]
root=repository/'frontend/dist/viteka-ui/browser'
artifacts=repository/'artifacts/ui';artifacts.mkdir(parents=True,exist_ok=True)
class Quiet(http.server.SimpleHTTPRequestHandler):
 def log_message(self,*args):pass
server=http.server.ThreadingHTTPServer(('127.0.0.1',0),functools.partial(Quiet,directory=str(root)))
threading.Thread(target=server.serve_forever,daemon=True).start()
url=f'http://127.0.0.1:{server.server_port}'
class AuthorityFixture:
 def __init__(self):
  self.accounts=[dict(id=f'{i:032x}',name=name,role=role,active=True,mustChangePassword=True,utilityIds=[],password=password) for i,(name,role,password) in enumerate([('Viteka','SuperAdministrator','jupiter'),('F. titular','Administrator','1234'),('usuario','User','0000')],1)]
  self.current=None;self.calls=[];self.prefs={};self.nix=dict(configured=False,saved=False,dsn='NIXFARMA',user='',dsnAvailable=True);self.probe_fail=False
 def view(self,u):
  if u is None:return None
  ready=not u['mustChangePassword'];admin=u['role'] in ('Administrator','SuperAdministrator')
  return {**{k:v for k,v in u.items() if k!='password'},'canConfigure':ready and u['role']=='SuperAdministrator','canManageUsers':ready and admin,'canInstallUpdates':ready and admin}
 def state(self):return dict(user=self.view(self.current))
 def invoke(self,msg):
  cmd=msg['command'];d=msg.get('payload') or {};self.calls.append(cmd)
  def deny():raise ValueError('No tienes permiso para esta acción.')
  if cmd=='auth.state':return self.state()
  if cmd=='auth.login':
   self.current=next((u for u in self.accounts if u['name'].lower()==d['name'].lower() and u['password']==d['password'] and u['active']),None)
   if not self.current:raise ValueError('Usuario o contraseña incorrectos.')
   return self.state()
  if cmd=='auth.logout':self.current=None;return self.state()
  if cmd=='auth.password':
   if not self.current or self.current['password']!=d['currentPassword']:raise ValueError('La contraseña actual no es correcta.')
   self.current.update(password=d['newPassword'],mustChangePassword=False);return self.state()
  pref=self.prefs.setdefault(self.current['id'] if self.current else 'anonymous',dict(openFavorites=False,sidebarCollapsed=False,theme='auto',zoomPercent=100))
  if cmd=='app.state':return dict(version='0.12.0',**pref)
  if cmd=='window.appearance':return {}
  if cmd=='window.zoom':pref['zoomPercent']=d['percent'];return dict(percent=d['percent'])
  if not self.current or self.current['mustChangePassword']:deny()
  role=self.current['role'];admin=role!='User';superadmin=role=='SuperAdministrator'
  if cmd.startswith('settings.'):
   prop={'start':'openFavorites','sidebar':'sidebarCollapsed','theme':'theme'}[cmd.split('.')[1]];pref[prop]=next(iter(d.values()));return {}
  if cmd=='utilities.list':return dict(utilities=[])
  if cmd=='users.list':
   if not admin:deny()
   return dict(users=[self.view(u) for u in self.accounts],utilities=[dict(id='fixture-a',name='Aplicación de prueba A'),dict(id='fixture-b',name='Aplicación de prueba B')])
  if cmd.startswith('users.'):
   target=next((u for u in self.accounts if u['id']==d.get('id')),None)
   if not admin or target and target['role']!='User' and not superadmin:deny()
   if cmd=='users.create':
    if d['role']!='User' and not superadmin:deny()
    self.accounts.append(dict(id=f'{len(self.accounts)+1:032x}',name=d['name'],role=d['role'],active=True,mustChangePassword=True,utilityIds=d['utilityIds'],password=d['password']));return {}
   if cmd=='users.modify':target.update(name=d['name'],role=d['role'],active=d['active'],utilityIds=d['utilityIds']);return self.state()
   if cmd=='users.password':target.update(password=d['password'],mustChangePassword=True);return {}
  if cmd.startswith('nixfarma.'):
   if not superadmin:deny()
   assert not d,'Oracle credentials/SQL must not travel through the web bridge'
   if cmd=='nixfarma.status':return copy.deepcopy(self.nix)
   if cmd=='nixfarma.configure':self.nix.update(configured=True,saved=True,user='fixture-oracle');return dict(state=copy.deepcopy(self.nix))
   if cmd=='nixfarma.forget':self.nix.update(configured=False,saved=False,user='');return dict(state=copy.deepcopy(self.nix))
   if cmd=='nixfarma.test':return dict(success=not self.probe_fail,message='Oracle ha rechazado el usuario o la contraseña.' if self.probe_fail else 'Conexión verificada: prueba de solo lectura correcta.',sqlState='28000' if self.probe_fail else None,oracleCode=1017 if self.probe_fail else None)
  if cmd.startswith('update.'):
   if cmd in ('update.state','update.check'):return dict(canInstallHere=True,available=True,version='0.13.0',lastChecked='2026-10-28T10:00:00Z')
   if cmd=='update.install':
    if not admin:deny()
    return dict(cancelled=True)
   if cmd=='update.publication':return dict(opened=True)
   if cmd=='update.history':return dict(local=[dict(kind='version_started',version='0.12.0',at='2026-10-28T10:00:00Z')],published=[dict(title='Viteka 0.12.0',version='0.12.0',notes='<script>window.historyScriptRan=true</script>',publishedAt='2026-10-28T10:00:00Z')])
  if cmd=='resource.open':return dict(opened=d['id']!='anydesk')
  if cmd=='clipboard.copy':return {}
  raise ValueError('Acción no disponible.')
fixture=AuthorityFixture()
with sync_playwright() as p:
 executable=os.environ.get('VITEKA_CHROME') or shutil.which('chromium') or shutil.which('google-chrome') or str(Path(os.environ.get('PROGRAMFILES','C:/Program Files'))/'Google/Chrome/Application/chrome.exe')
 browser=p.chromium.launch(executable_path=executable,headless=True)
 context=browser.new_context(viewport={'width':1240,'height':820})
 def native_call(source,msg):
  try:return dict(id=msg['id'],ok=True,result=fixture.invoke(msg))
  except ValueError as e:return dict(id=msg['id'],ok=False,error=str(e))
 context.expose_binding('fixtureNative',native_call)
 context.add_init_script('''window.chrome=window.chrome||{};let listener;window.chrome.webview={addEventListener:(type,fn)=>listener=fn,postMessage:msg=>{window.fixtureNative(msg).then(response=>{if(msg.command==='window.zoom'&&response.ok)document.documentElement.style.zoom=String(response.result.percent/100);listener({data:response});});}};''')
 page=context.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)));page.on('console',lambda e:errors.append(e.text) if e.type=='error' else None)
 page.route('**/favicon.ico',lambda route:route.fulfill(status=204));page.goto(url)
 page.get_by_role('heading',name='Iniciar sesión',exact=True).wait_for()
 assert page.get_by_role('navigation').count()==0
 assert page.get_by_role('heading',name='Conexión a NixFarma').count()==0
 page.screenshot(path=str(artifacts/'angular-login.png'),full_page=True)
 def login(name,password,new_password=None):
  page.get_by_label('Usuario',exact=True).fill(name);page.get_by_label('Contraseña',exact=True).fill(password);page.get_by_role('button',name='Entrar',exact=True).click()
  if new_password:
   page.get_by_role('heading',name='Cambia tu contraseña',exact=True).wait_for()
   assert page.get_by_role('navigation').count()==0
   page.get_by_label('Contraseña actual',exact=True).fill(password);page.get_by_label('Nueva contraseña',exact=True).fill(new_password);page.get_by_label('Repetir nueva contraseña',exact=True).fill(new_password);page.get_by_role('button',name='Guardar contraseña',exact=True).click()
  page.get_by_role('heading',name='Portal de soporte',exact=True).wait_for()
 def go(name):
  if name in ('Mi cuenta','Usuarios'):
   go('Configuración');page.locator('.setting-row').filter(has=page.get_by_text(name,exact=True)).click()
  else:page.get_by_role('navigation').get_by_role('button',name=name,exact=True).click()
  page.get_by_role('heading',name=name,exact=True).wait_for()
 def appearance(theme):
  page.locator('.setting-row').filter(has=page.get_by_text('Apariencia',exact=True)).click();dialog=page.get_by_role('dialog',name='Apariencia',exact=True);dialog.get_by_label('Apariencia',exact=True).select_option(theme);dialog.get_by_role('button',name='Guardar cambios',exact=True).click();dialog.wait_for(state='hidden')
 login('Viteka','jupiter','FixtureSuperPass1')
 assert page.get_by_label('Apariencia',exact=True).count()==0
 page.screenshot(path=str(artifacts/'angular-desktop.png'),full_page=True)
 page.keyboard.down('Control');page.mouse.wheel(0,-120);page.keyboard.up('Control');page.wait_for_function("() => document.documentElement.style.zoom==='1.1'")
 page.keyboard.press('Control+0');page.wait_for_function("() => document.documentElement.style.zoom==='1'")
 page.get_by_role('button',name='Cerrar menú lateral',exact=True).click();page.wait_for_function("() => Math.round(document.querySelector('aside').getBoundingClientRect().width)===76")
 page.reload();page.get_by_role('button',name='Abrir menú lateral',exact=True).wait_for();page.get_by_role('button',name='Abrir menú lateral',exact=True).click()
 go('Mi cuenta');appearance('dark');page.wait_for_function("() => document.documentElement.dataset.theme==='dark'")
 page.reload();page.wait_for_function("() => document.documentElement.dataset.theme==='dark'")
 page.get_by_role('navigation').get_by_role('button',name='Inicio',exact=True).click();page.get_by_role('button',name='Cerrar menú lateral',exact=True).click();page.wait_for_function("() => Math.round(document.querySelector('aside').getBoundingClientRect().width)===76")
 assert page.locator('.brand-bottom .isologo').evaluate('el=>getComputedStyle(el).filter')=='brightness(0) invert(1)'
 page.screenshot(path=str(artifacts/'angular-dark.png'),full_page=True)
 page.get_by_role('button',name='Abrir menú lateral',exact=True).click();go('Configuración')
 assert page.get_by_role('button',name='Buscar actualizaciones',exact=True).count()==0
 assert page.get_by_label('Apariencia',exact=True).count()==0
 assert page.locator('input[type=password]').count()==0
 page.locator('.setting-row').filter(has=page.get_by_text('Conexión a NixFarma',exact=True)).click()
 page.get_by_role('button',name='Configurar conexión',exact=True).click();page.get_by_text('Conexión guardada. Pulsa Probar conexión para verificarla.',exact=True).wait_for()
 page.get_by_role('button',name='Probar conexión',exact=True).click();page.get_by_text('Conexión verificada: prueba de solo lectura correcta.',exact=True).wait_for()
 fixture.probe_fail=True;page.get_by_role('button',name='Probar conexión',exact=True).click();page.get_by_text('Diagnóstico: 28000 · ORA-01017',exact=True).wait_for()
 page.keyboard.press('Escape');go('Actualizaciones');page.get_by_role('button',name='Buscar actualizaciones',exact=True).click();page.get_by_text('Versión 0.13.0 disponible.',exact=True).wait_for()
 page.get_by_role('button',name='Descargar e instalar',exact=True).click();page.get_by_text('Instalación cancelada. Puedes seguir usando esta versión.',exact=True).wait_for()
 page.get_by_role('button',name='Historial de actualizaciones',exact=True).click();page.get_by_role('dialog',name='Historial de actualizaciones',exact=True).wait_for();page.get_by_text('Versión ejecutada 0.12.0',exact=True).wait_for()
 page.get_by_role('dialog').get_by_role('group').count() if False else None
 page.get_by_role('dialog').locator('summary').click();assert '<script>' in page.get_by_role('dialog').locator('pre').inner_text();assert page.evaluate('window.historyScriptRan===undefined')
 page.keyboard.press('Escape');page.get_by_role('dialog').wait_for(state='hidden')
 go('Usuarios');page.get_by_role('button',name='Crear usuario',exact=True).click();editor=page.get_by_role('dialog',name='Editar cuenta y permisos',exact=True)
 editor.get_by_label('Nombre de usuario',exact=True).fill('Personal');editor.get_by_label('Contraseña inicial',exact=True).fill('FixtureStaffPass1');editor.get_by_label('Repetir contraseña inicial',exact=True).fill('FixtureStaffPass1');editor.get_by_label('Aplicación de prueba A',exact=True).check();editor.get_by_role('button',name='Guardar cuenta',exact=True).click();editor.wait_for(state='hidden');page.get_by_role('button',name='Editar Personal',exact=True).wait_for()
 page.screenshot(path=str(artifacts/'angular-users.png'),full_page=True)
 go('Mi cuenta');appearance('light')
 page.get_by_role('button',name='Cerrar sesión',exact=True).click();page.get_by_role('heading',name='Iniciar sesión',exact=True).wait_for()
 login('F. titular','1234','FixtureAdminPass1')
 assert page.get_by_role('navigation').get_by_role('button',name='Configuración',exact=True).count()==1
 go('Configuración');assert page.locator('.setting-row').filter(has=page.get_by_text('Conexión a NixFarma',exact=True)).count()==0
 go('Usuarios');assert page.get_by_role('button',name='Editar Viteka',exact=True).count()==0
 page.get_by_role('button',name='Crear usuario',exact=True).click();editor=page.get_by_role('dialog',name='Editar cuenta y permisos',exact=True);editor.wait_for();expect(editor.get_by_label('Rol',exact=True).locator('option')).to_have_count(1);page.keyboard.press('Escape')
 page.get_by_role('button',name='Cerrar sesión',exact=True).click();page.get_by_role('heading',name='Iniciar sesión',exact=True).wait_for()
 login('usuario','0000','FixtureUserPass1')
 assert page.get_by_role('navigation').get_by_role('button',name='Usuarios',exact=True).count()==0
 assert page.get_by_role('navigation').get_by_role('button',name='Configuración',exact=True).count()==1
 go('Configuración');assert page.locator('.setting-row').filter(has=page.get_by_text('Conexión a NixFarma',exact=True)).count()==0
 go('Aplicaciones');page.get_by_role('heading',name='No hay aplicaciones disponibles para tu cuenta',exact=True).wait_for()
 go('Actualizaciones');assert page.get_by_role('button',name='Descargar e instalar',exact=True).count()==0;page.get_by_text('El titular o Viteka pueden ejecutar esta actualización desde el servidor de la farmacia.',exact=True).wait_for()
 go('Mi cuenta');page.locator('.setting-row').filter(has=page.get_by_text('Cambiar mi contraseña',exact=True)).click();page.get_by_label('Contraseña actual',exact=True).fill('FixtureUserPass1');page.get_by_label('Nueva contraseña',exact=True).fill('abc');page.get_by_label('Repetir nueva contraseña',exact=True).fill('abc');page.get_by_role('button',name='Cambiar mi contraseña',exact=True).click();page.get_by_text('Contraseña actualizada.',exact=True).wait_for()
 expect(page.get_by_role('dialog')).to_have_count(0)
 page.locator('.setting-row').filter(has=page.get_by_text('Apariencia',exact=True)).click();page.get_by_role('dialog',name='Apariencia',exact=True).get_by_label('Apariencia',exact=True).select_option('dark');page.get_by_role('button',name='Cancelar',exact=True).click();assert page.evaluate('document.documentElement.dataset.theme')=='light'
 page.screenshot(path=str(artifacts/'angular-account-rows.png'),full_page=True)
 page.locator('.setting-row').filter(has=page.get_by_text('Cambiar mi contraseña',exact=True)).click();page.screenshot(path=str(artifacts/'angular-password-dialog.png'),full_page=True);page.keyboard.press('Escape')
 page.set_viewport_size({'width':820,'height':600});assert page.evaluate('document.documentElement.scrollWidth<=innerWidth')
 assert not errors,errors
 browser.close()
server.shutdown()
print('PASS: login and first-password change; role navigation; per-user preferences; hidden Oracle configuration; connection messages; users and grants editor; administrator restrictions; update tab and history; escaped release notes; own-password change; compact layout; no JS errors.')
