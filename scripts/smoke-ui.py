from pathlib import Path
import threading,functools,http.server,os,shutil,re,copy,json
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
  self.current=None;self.calls=[];self.prefs={};self.nix=dict(configured=False,saved=False,dsn='NIXFARMA',user='',dsnAvailable=True);self.probe_fail=False;self.schema_fail=False;self.pharmacy=None;self.tenencia_ready=False;self.stock_error=False;self.tenencia_queries=0;self.many_groups=False;self.transport_error=False;self.use_excel=False;self.sort_cases=False
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
  if cmd=='utilities.list':return dict(utilities=[dict(id='tenencia-obligatoria',name='Tenencia obligatoria',description='Comprueba las existencias mínimas del listado de tu provincia.')] if admin or 'tenencia-obligatoria' in self.current['utilityIds'] else [])
  if cmd=='users.list':
   if not admin:deny()
   return dict(users=[self.view(u) for u in self.accounts],utilities=[dict(id='tenencia-obligatoria',name='Tenencia obligatoria'),dict(id='fixture-a',name='Aplicación de prueba A'),dict(id='fixture-b',name='Aplicación de prueba B')])
  if cmd.startswith('users.'):
   target=next((u for u in self.accounts if u['id']==d.get('id')),None)
   if not admin or target and target['role']!='User' and not superadmin:deny()
   if cmd=='users.create':
    if d['role']!='User' and not superadmin:deny()
    self.accounts.append(dict(id=f'{len(self.accounts)+1:032x}',name=d['name'],role=d['role'],active=True,mustChangePassword=True,utilityIds=d['utilityIds'],password=d['password']));return {}
   if cmd=='users.modify':target.update(name=d['name'],role=d['role'],active=d['active'],utilityIds=d['utilityIds']);return self.state()
   if cmd=='users.password':target.update(password=d['password'],mustChangePassword=True);return {}
  if cmd.startswith('pharmacy.'):
   if not admin:deny()
   if cmd=='pharmacy.save':self.pharmacy=copy.deepcopy(d)
   return dict(profile=copy.deepcopy(self.pharmacy),provinces=[dict(code='29',name='Málaga'),dict(code='28',name='Madrid')])
  if cmd.startswith('tenencia.'):
   if cmd=='tenencia.schema':
    if not superadmin:deny()
    return dict(code='column',message='Comprobación de tablas: Oracle no reconoce una columna. Diagnóstico: column · SQLSTATE 42000 · ORA-00904') if self.schema_fail else dict(code='schema_verified',message='Estructura verificada: ARTICULOS y EXISTENCIAS; columna de stock ACTUAL.')
   if not admin and 'tenencia-obligatoria' not in self.current['utilityIds']:deny()
   if self.pharmacy is None:return dict(code='pharmacy_pending',message='El titular o Viteka deben guardar la provincia en Datos de la farmacia.',province='')
   if not self.tenencia_ready:return dict(code='catalog_pending',message='Lista de tenencia pendiente para Málaga. No se ha evaluado el cumplimiento.',province='29',provinceName='Málaga')
   groups=[dict(id='a',name='Grupo ficticio A',minimum=3,stock=1,missing=2,status='shortfall',articles=[dict(code='650001',description='Artículo ficticio A1',stock=1),dict(code='650004',description='Artículo ficticio A2',stock=0)]),dict(id='b',name='Grupo ficticio B',minimum=1,stock=3,missing=0,status='review',requiresReview=True,selectionNote='Comprobar cada dosis.',articles=[dict(code='650002',description='Artículo con dos fichas',stock=3,issue='summed_stock')]),dict(id='c',name='Grupo ficticio C',minimum=1,stock=2,missing=0,status='covered',articles=[dict(code='650003',description='Artículo suficiente',stock=2)])]
   if self.sort_cases:groups[0].update(stock=12,missing=0,status='covered',articles=[dict(code='65000'+str(i),description=n,stock=v,issue=x) for i,n,v,x in [(1,'Z',10,None),(2,'B',0,'stock_not_found'),(3,'A',2,None),(4,'C',0,None)]])
   if self.use_excel:
    catalog=json.loads((repository/'src/Viteka.Server/Catalogs/tenencia/29.json').read_text(encoding='utf-8'))
    groups=[dict(id=g['id'],name=g['name'],minimum=g['minimum'],stock=0,missing=g['minimum'],status='review' if g.get('requiresReview') else 'shortfall',classification=g.get('atcPrefix') or g.get('classificationCode'),selectionNote=g.get('selectionNote'),sourceRow=g['sourceRow'],requiresReview=g.get('requiresReview',False),articles=[dict(code=c,description=g.get('descriptions',{}).get(c,'Artículo CN '+c),stock=0,issue='not_found') for c in g['codes']]) for g in catalog['groups']]
   if self.many_groups:groups.extend(dict(id='extra-'+str(i),name='Grupo adicional '+str(i),minimum=1,stock=1,missing=0,status='covered',articles=[dict(code=str(650000+i),description='Medicamento adicional '+str(i),stock=1)]) for i in range(4,31))
   preview=[dict(**{**r,'stock':None,'missing':None,'status':'not_checked','articles':[dict(**{**a,'stock':None,'issue':'not_checked'}) for a in r['articles']]}) for r in groups]
   response=dict(code='ready',message='Lista disponible. Pulsa Consultar para leer las existencias actuales.',province=self.pharmacy['province'],provinceName='Málaga',listVersion='Fixture de pruebas',source='Datos ficticios exclusivos del smoke UI',rows=preview)
   if cmd=='tenencia.query':
    self.tenencia_queries+=1
    if self.transport_error:raise ValueError('Servicio no disponible.')
    if self.stock_error:return dict(**{**response,'code':'network','message':'No se alcanza el servicio Oracle.'})
    response.update(code='ok',message='Consulta de solo lectura completada.',checkedAt='2026-10-28T12:00:00Z',rows=groups)
   return response
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
  if name in ('Mi cuenta','Usuarios','Actualizaciones','Auditoría'):
   go('Configuración');page.locator('.setting-row').filter(has=page.get_by_text(name,exact=True)).click()
  else:page.get_by_role('navigation').get_by_role('button',name=name,exact=True).click()
  page.get_by_role('heading',name=name,exact=True).wait_for()
 def appearance(theme):
  page.locator('.setting-row').filter(has=page.get_by_text('Apariencia',exact=True)).click();dialog=page.get_by_role('dialog',name='Apariencia',exact=True);dialog.get_by_label('Apariencia',exact=True).select_option(theme);dialog.get_by_role('button',name='Guardar cambios',exact=True).click();dialog.wait_for(state='hidden')
 login('Viteka','jupiter','FixtureSuperPass1')
 assert page.get_by_label('Apariencia',exact=True).count()==0
 logout=page.get_by_role('button',name='Cerrar sesión',exact=True);brand=page.get_by_role('button',name='Web Viteka',exact=True);assert logout.bounding_box()['y']>page.get_by_role('navigation').bounding_box()['y']+page.get_by_role('navigation').bounding_box()['height'];assert logout.bounding_box()['y']+logout.bounding_box()['height']+12<=brand.bounding_box()['y'];assert page.locator('.logout-button v-icon svg').count()==1
 assert page.get_by_role('navigation').get_by_role('button',name='Actualizaciones',exact=True).count()==0
 page.screenshot(path=str(artifacts/'angular-desktop.png'),full_page=True)
 page.keyboard.down('Control');page.mouse.wheel(0,-120);page.keyboard.up('Control');page.wait_for_function("() => document.documentElement.style.zoom==='1.1'")
 page.keyboard.press('Control+0');page.wait_for_function("() => document.documentElement.style.zoom==='1'")
 page.get_by_role('button',name='Cerrar menú lateral',exact=True).click();page.wait_for_function("() => Math.round(document.querySelector('aside').getBoundingClientRect().width)===76")
 page.reload();page.get_by_role('button',name='Abrir menú lateral',exact=True).wait_for();page.get_by_role('button',name='Abrir menú lateral',exact=True).click()
 go('Mi cuenta');appearance('dark');page.wait_for_function("() => document.documentElement.dataset.theme==='dark'")
 page.reload();page.wait_for_function("() => document.documentElement.dataset.theme==='dark'")
 page.get_by_role('navigation').get_by_role('button',name='Inicio',exact=True).click();page.get_by_role('button',name='Cerrar menú lateral',exact=True).click();page.wait_for_function("() => Math.round(document.querySelector('aside').getBoundingClientRect().width)===76")
 assert page.locator('.logout-button').bounding_box()['width']<=60
 assert page.locator('.brand-bottom .isologo').evaluate('el=>getComputedStyle(el).filter')=='brightness(0) invert(1)'
 page.screenshot(path=str(artifacts/'angular-dark.png'),full_page=True)
 page.get_by_role('button',name='Abrir menú lateral',exact=True).click();go('Configuración')
 assert page.get_by_role('button',name='Buscar actualizaciones',exact=True).count()==0
 assert page.get_by_label('Apariencia',exact=True).count()==0
 assert page.locator('input[type=password]').count()==0
 page.locator('.setting-row').filter(has=page.get_by_text('Conexión a NixFarma',exact=True)).click()
 page.get_by_role('button',name='Configurar conexión',exact=True).click();page.get_by_text('Conexión guardada. Pulsa Probar conexión para verificarla.',exact=True).wait_for()
 page.get_by_role('button',name='Probar conexión',exact=True).click();page.get_by_text('Conexión verificada: prueba de solo lectura correcta.',exact=True).wait_for()
 fixture.schema_fail=True;page.get_by_role('button',name='Comprobar tablas de tenencia',exact=True).click();page.get_by_text('Comprobación de tablas: Oracle no reconoce una columna. Diagnóstico: column · SQLSTATE 42000 · ORA-00904',exact=True).wait_for();expect(page.locator('.connection-result.verified')).to_have_count(0);assert page.get_by_text('Conexión verificada: prueba de solo lectura correcta.',exact=True).count()==0
 page.get_by_role('button',name='Probar conexión',exact=True).click();page.get_by_text('Conexión verificada: prueba de solo lectura correcta.',exact=True).wait_for();assert page.get_by_text('Comprobación de tablas: Oracle no reconoce una columna. Diagnóstico: column · SQLSTATE 42000 · ORA-00904',exact=True).count()==0
 fixture.probe_fail=True;page.get_by_role('button',name='Probar conexión',exact=True).click();page.get_by_text('Diagnóstico: 28000 · ORA-01017',exact=True).wait_for();expect(page.locator('.connection-result.verified')).to_have_count(0)
 page.keyboard.press('Escape');go('Auditoría');page.get_by_role('heading',name='Todavía no hay registros de auditoría',exact=True).wait_for();assert page.get_by_role('navigation').get_by_role('button',name='Auditoría',exact=True).count()==0;go('Configuración');page.screenshot(path=str(artifacts/'angular-settings.png'),full_page=True);go('Actualizaciones');page.get_by_role('button',name='Buscar actualizaciones',exact=True).click();page.get_by_text('Versión 0.13.0 disponible.',exact=True).wait_for()
 assert page.get_by_role('navigation').get_by_role('button',name='Configuración',exact=True).get_attribute('aria-current')=='page'
 page.get_by_role('button',name='Actualizar ahora',exact=True).click();page.get_by_text('Instalación cancelada. Puedes seguir usando esta versión.',exact=True).wait_for()
 page.get_by_role('button',name='Historial de actualizaciones',exact=True).click();page.get_by_role('dialog',name='Historial de actualizaciones',exact=True).wait_for();page.get_by_text('Versión ejecutada 0.12.0',exact=True).wait_for()
 page.get_by_role('dialog').get_by_role('group').count() if False else None
 page.get_by_role('dialog').locator('summary').click();assert '<script>' in page.get_by_role('dialog').locator('pre').inner_text();assert page.evaluate('window.historyScriptRan===undefined')
 page.keyboard.press('Escape');page.get_by_role('dialog').wait_for(state='hidden')
 go('Aplicaciones');page.get_by_role('button',name='Tenencia obligatoria',exact=False).click();page.get_by_role('heading',name='Tenencia obligatoria',exact=True).wait_for();page.get_by_text('El titular o Viteka deben guardar la provincia en Datos de la farmacia.',exact=True).wait_for();assert fixture.tenencia_queries==0
 go('Configuración');page.locator('.setting-row').filter(has=page.get_by_text('Datos de la farmacia',exact=True)).click();pharmacy=page.get_by_role('dialog',name='Datos de la farmacia',exact=True);pharmacy.get_by_label('Nombre de la farmacia',exact=True).fill('Farmacia ficticia');pharmacy.get_by_label('Provincia',exact=True).select_option('29');pharmacy.get_by_label('Localidad',exact=True).fill('Málaga');pharmacy.get_by_label('Correo electrónico',exact=True).fill('fixture@example.test');pharmacy.get_by_role('button',name='Guardar datos',exact=True).click();pharmacy.get_by_text('Datos guardados para toda la farmacia. La próxima consulta utilizará esta provincia.',exact=True).wait_for();page.screenshot(path=str(artifacts/'angular-pharmacy.png'),full_page=True);page.keyboard.press('Escape');pharmacy.wait_for(state='hidden')
 go('Aplicaciones');page.get_by_role('button',name='Tenencia obligatoria',exact=False).click();page.get_by_text('Lista de tenencia pendiente para Málaga. No se ha evaluado el cumplimiento.',exact=True).wait_for();expect(page.get_by_role('button',name='Consultar existencias',exact=True)).to_be_disabled();assert page.locator('table').count()==0
 fixture.tenencia_ready=True;page.get_by_role('button',name='Revisar disponibilidad',exact=True).click();expect(page.get_by_role('button',name='Consultar existencias',exact=True)).to_be_enabled();assert fixture.tenencia_queries==0
 expect(page.locator('.group-button')).to_have_count(3);expect(page.locator('.tenencia-medications tbody tr')).to_have_count(2);expect(page.locator('.drug-stock').first).to_have_text('Sin consultar');page.get_by_role('button',name='Seleccionar grupo Grupo ficticio B',exact=True).click();expect(page.locator('.tenencia-medications tbody tr')).to_have_count(1);page.get_by_role('button',name='Seleccionar grupo Grupo ficticio A',exact=True).click()
 page.get_by_role('button',name='Consultar existencias',exact=True).click();page.get_by_role('button',name='Ver detalle de Grupo ficticio A',exact=True).wait_for();assert fixture.tenencia_queries==1;page.screenshot(path=str(artifacts/'angular-tenencia.png'),full_page=True)
 fixture.many_groups=True;page.get_by_role('button',name='Revisar disponibilidad',exact=True).click();expect(page.locator('.group-button')).to_have_count(30);page.get_by_role('button',name='Seleccionar grupo Grupo adicional 30',exact=True).click();page.get_by_role('region',name='Medicamentos del grupo',exact=True).get_by_text('Medicamento adicional 30',exact=True).wait_for();expect(page.locator('.drug-stock').first).to_have_text('Sin consultar');fixture.many_groups=False;page.get_by_role('button',name='Revisar disponibilidad',exact=True).click();expect(page.locator('.group-button')).to_have_count(3);page.get_by_role('button',name='Consultar existencias',exact=True).click();page.get_by_role('button',name='Ver detalle de Grupo ficticio A',exact=True).wait_for()
 page.set_viewport_size({'width':820,'height':680});assert page.evaluate('document.documentElement.scrollWidth<=innerWidth');page.set_viewport_size({'width':1240,'height':820})
 page.get_by_role('button',name='Seleccionar grupo Grupo ficticio B',exact=True).click();page.get_by_role('button',name='Ver detalle de Grupo ficticio B',exact=True).click();page.get_by_role('dialog',name='Detalle de tenencia',exact=True).get_by_text('Varias fichas de existencias: se muestra la suma.',exact=True).wait_for();page.keyboard.press('Escape');expect(page.get_by_role('dialog')).to_have_count(0)
 page.get_by_label('Estado',exact=True).select_option('shortfall');expect(page.locator('.group-button')).to_have_count(1);expect(page.locator('.tenencia-medications tbody tr')).to_have_count(2);page.get_by_label('Buscar grupo, artículo o CN',exact=True).fill('650099');page.get_by_text('No hay grupos que coincidan con estos filtros.',exact=True).wait_for();page.get_by_label('Buscar grupo, artículo o CN',exact=True).fill('');page.get_by_label('Estado',exact=True).select_option('all')
 fixture.stock_error=True;page.get_by_role('button',name='Consultar existencias',exact=True).click();page.get_by_text('No se alcanza el servicio Oracle.',exact=True).wait_for();expect(page.locator('.group-button')).to_have_count(3);expect(page.locator('.drug-stock').first).to_have_text('Sin consultar');fixture.stock_error=False
 page.get_by_label('Estado',exact=True).select_option('covered');fixture.transport_error=True;page.get_by_role('button',name='Consultar existencias',exact=True).click();page.get_by_text('Servicio no disponible.',exact=True).wait_for();expect(page.locator('.group-button')).to_have_count(3);expect(page.locator('.drug-stock').first).to_have_text('Sin consultar');fixture.transport_error=False
 fixture.sort_cases=True;go('Aplicaciones');page.get_by_role('button',name='Tenencia obligatoria',exact=False).click();page.get_by_role('button',name='Consultar existencias',exact=True).click();expect(page.locator('.drug-stock')).to_have_count(4)
 meds=page.get_by_role('region',name='Medicamentos del grupo',exact=True)
 sortstock=meds.get_by_role('columnheader',name=re.compile('Existencias')).get_by_role('button');sortstock.click();expect(page.locator('.drug-stock')).to_have_text(['0','0','2','10']);sortstock.click();expect(page.locator('.drug-stock')).to_have_text(['10','2','0','0']);assert page.locator('.drug-stock',has_text='Pendiente').count()==0
 meds.get_by_role('columnheader',name=re.compile('Medicamento')).get_by_role('button').click();expect(page.locator('.article-select')).to_have_text(['A','B','C','Z'])
 page.get_by_text('Sin ficha de existencias en NixFarma: cuenta como 0.',exact=True).wait_for();assert page.get_by_text('Pendiente no significa cero',exact=False).count()==0
 groups=page.get_by_role('region',name='Grupos de tenencia',exact=True);groups.get_by_role('columnheader',name=re.compile('Mín.')).get_by_role('button').click();expect(page.locator('.groups-table tbody tr td:nth-child(3)')).to_have_text(['1','1','3']);assert page.get_by_role('button',name='Seleccionar grupo Grupo ficticio A',exact=True).get_attribute('aria-pressed')=='true';fixture.sort_cases=False
 fixture.use_excel=True;go('Aplicaciones');page.get_by_role('button',name='Tenencia obligatoria',exact=False).click();expect(page.locator('.group-button')).to_have_count(83)
 expect(page.locator('.page-head')).to_have_count(0);expect(page.locator('footer')).to_have_count(0)
 for size in [{'width':1366,'height':768},{'width':1920,'height':1080}]:
  page.set_viewport_size(size)
  page.wait_for_timeout(100)
  geometry=page.evaluate('''() => {const table=document.querySelector('.tenencia-workspace').getBoundingClientRect(),info=document.querySelector('.tenencia-information').getBoundingClientRect();return {height:table.height,top:table.top,bottom:info.bottom,width:document.documentElement.scrollWidth,viewport:innerWidth,viewportHeight:innerHeight,scroll:document.documentElement.scrollHeight};}''')
  assert geometry['height']>=280 and geometry['top']<250 and geometry['bottom']<=size['height'] and geometry['width']<=size['width'] and geometry['scroll']<=size['height']+1,geometry
  page.screenshot(path=str(artifacts/f"angular-tenencia-{size['width']}.png"),full_page=True)
 page.set_viewport_size({'width':1366,'height':768});page.evaluate("document.documentElement.dataset.theme='light'");page.screenshot(path=str(artifacts/'angular-tenencia-light.png'),full_page=True);page.evaluate("document.documentElement.dataset.theme='dark'");page.set_viewport_size({'width':1240,'height':820})
 page.get_by_role('button',name='Seleccionar artículo 152195',exact=True).click();page.get_by_role('region',name='Información de la selección',exact=True).get_by_text('Artículo · CN 152195',exact=True).wait_for();page.keyboard.press('ArrowDown');page.get_by_role('region',name='Información de la selección',exact=True).get_by_text('Artículo · CN 349696',exact=True).wait_for()
 expect(page.locator('.groups-table tbody tr').filter(has=page.get_by_role('button',name='Seleccionar grupo DIGOXINA ORAL 2 ENVASES',exact=True)).locator('.group-code')).to_have_text('C01AA05');page.get_by_role('button',name='Seleccionar grupo DIGOXINA ORAL 2 ENVASES',exact=True).wait_for();page.get_by_role('button',name='Seleccionar grupo DIGOXINA INYECTABLE 1 ENVASE',exact=True).click();page.get_by_role('region',name='Información de la selección',exact=True).get_by_text('C01AA05',exact=True).wait_for()
 page.get_by_role('button',name='Seleccionar grupo HOJA BISTURÍ CON MANGO 1 UNIDAD',exact=True).click();page.get_by_role('region',name='Medicamentos del grupo',exact=True).get_by_text('175550',exact=True).wait_for();expect(page.locator('.tenencia-medications tbody tr')).to_have_count(18);page.screenshot(path=str(artifacts/'angular-tenencia-excel.png'),full_page=True);page.get_by_role('button',name='Seleccionar grupo BOLSA DE RECOGIDA DE ORINA PEDIÁTRICA 2 UNIDADES',exact=True).click();expect(page.locator('.tenencia-medications tbody tr')).to_have_count(3);page.get_by_role('region',name='Medicamentos del grupo',exact=True).get_by_text('165727',exact=True).wait_for();fixture.use_excel=False
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
 assert page.locator('.setting-row').filter(has=page.get_by_text('Auditoría',exact=True)).count()==0
 go('Aplicaciones');page.get_by_role('heading',name='No hay aplicaciones disponibles para tu cuenta',exact=True).wait_for()
 fixture.current['utilityIds']=['tenencia-obligatoria'];page.reload();go('Aplicaciones');page.get_by_role('button',name='Tenencia obligatoria',exact=False).click();page.get_by_role('heading',name='Tenencia obligatoria',exact=True).wait_for()
 go('Actualizaciones');assert page.get_by_role('button',name='Actualizar ahora',exact=True).count()==0;page.get_by_text('El titular o Viteka pueden ejecutar esta actualización desde el servidor de la farmacia.',exact=True).wait_for()
 go('Mi cuenta');page.locator('.setting-row').filter(has=page.get_by_text('Cambiar mi contraseña',exact=True)).click();page.get_by_label('Contraseña actual',exact=True).fill('FixtureUserPass1');page.get_by_label('Nueva contraseña',exact=True).fill('abc');page.get_by_label('Repetir nueva contraseña',exact=True).fill('abc');page.get_by_role('button',name='Cambiar mi contraseña',exact=True).click();page.get_by_text('Contraseña actualizada.',exact=True).wait_for()
 expect(page.get_by_role('dialog')).to_have_count(0)
 page.locator('.setting-row').filter(has=page.get_by_text('Apariencia',exact=True)).click();page.get_by_role('dialog',name='Apariencia',exact=True).get_by_label('Apariencia',exact=True).select_option('dark');page.get_by_role('button',name='Cancelar',exact=True).click();assert page.evaluate('document.documentElement.dataset.theme')=='light'
 page.screenshot(path=str(artifacts/'angular-account-rows.png'),full_page=True)
 page.locator('.setting-row').filter(has=page.get_by_text('Cambiar mi contraseña',exact=True)).click();page.screenshot(path=str(artifacts/'angular-password-dialog.png'),full_page=True);page.keyboard.press('Escape')
 page.set_viewport_size({'width':820,'height':600});assert page.evaluate('document.documentElement.scrollWidth<=innerWidth')
 assert not errors,errors
 browser.close()
server.shutdown()
print('PASS: shared pharmacy profile; provincial catalog pending; explicit tenencia query; results, filters and detail; stale results cleared on error; utility access grant; login and first-password change; role navigation; per-user preferences; hidden Oracle configuration; connection messages; users and grants editor; administrator restrictions; update tab and history; escaped release notes; own-password change; compact layout; no JS errors.')
