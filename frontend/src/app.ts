import { Component, computed, inject, signal, effect, DestroyRef, HostListener } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  DesktopBridge,
  AppState,
  UserView,
  Role,
  Utility,
  UpdateState,
  UpdateHistory,
  NixFarmaState,
  NixFarmaResponse,
} from './bridge';
import { AuditComponent } from './settings/audit';
import { IconComponent } from './icon';
import { PharmacySettingsComponent } from './settings/pharmacy';
import { TenenciaComponent } from './apps/tenencia/tenencia';
type Page =
  | 'Inicio'
  | 'Aplicaciones'
  | 'Actualizaciones'
  | 'Favoritos'
  | 'Auditoría'
  | 'Usuarios'
  | 'Utilidades'
  | 'Mi cuenta'
  | 'Configuración';
type NavItem = { name: string; icon: string; page: Page; utility?: string };
type Command = { label: string; hint: string; icon: string; run: () => void };
export type TenenciaSummary = { total: number; covered: number; shortfall: number; checkedAt: string };
const TENENCIA = 'tenencia-obligatoria';
const SETTINGS_PAGES: Page[] = ['Configuración', 'Mi cuenta', 'Usuarios', 'Utilidades', 'Actualizaciones', 'Auditoría'];
@Component({
  selector: 'viteka-app',
  standalone: true,
  imports: [IconComponent, FormsModule, PharmacySettingsComponent, TenenciaComponent, AuditComponent],
  templateUrl: './app.html',
})
export class AppComponent {
  readonly bridge = inject(DesktopBridge);
  readonly user = this.bridge.user;
  readonly page = signal<Page>('Inicio');
  readonly version = signal('');
  readonly reviewMode = signal(false);
  readonly reviewDeviceId = signal('');
  async copyReviewId() {
    try {
      await this.bridge.call('review.copyId');
      this.notify('Identificador del PC copiado.');
    } catch (e) {
      this.error.set(this.message(e));
    }
  }
  readonly loading = signal(true);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly nav = computed<NavItem[]>(() => [
    { name: 'Inicio', icon: 'home', page: 'Inicio' },
    { name: 'Aplicaciones', icon: 'grid', page: 'Aplicaciones' },
    ...(this.user()?.role !== 'User' ? [{ name: 'Favoritos', icon: 'star', page: 'Favoritos' as Page }] : []),
    { name: 'Configuración', icon: 'settings', page: 'Configuración' },
  ]);
  /** Accesos directos de la barra lateral: las utilidades que el usuario marca con la estrella. */
  readonly shortcuts = computed<NavItem[]>(() =>
    this.favoriteUtilities().map((u) => ({
      name: u.name,
      icon: this.utilityIcon(u.id),
      page: 'Aplicaciones' as Page,
      utility: u.id,
    })),
  );
  readonly hasTenencia = computed(() => this.utilities().some((u) => u.id === TENENCIA));
  readonly favorites = signal<string[]>([]);
  readonly favoriteUtilities = computed(() => this.utilities().filter((u) => this.favorites().includes(u.id)));
  readonly tenenciaSummary = signal<TenenciaSummary | null>(null);
  readonly ready = signal(false);
  readonly updatePrompt = signal(false);
  readonly paletteOpen = signal(false);
  readonly paletteQuery = signal('');
  readonly paletteIndex = signal(0);
  readonly tenenciaSearch = signal('');
  readonly toastKind = signal<'ok' | 'warn'>('ok');
  readonly settingsTabs = computed(() => [
    { label: 'Mi cuenta', page: 'Mi cuenta' as Page },
    ...(this.user()?.canManageUsers
      ? [
          { label: 'Usuarios', page: 'Usuarios' as Page },
          { label: 'Acceso a aplicaciones', page: 'Utilidades' as Page },
          { label: 'Auditoría', page: 'Auditoría' as Page },
        ]
      : []),
    { label: 'Actualizaciones', page: 'Actualizaciones' as Page },
  ]);
  readonly commands = computed<Command[]>(() => {
    const u = this.user();
    if (!u) return [];
    const list: Command[] = [
      { label: 'Inicio', hint: 'Asistencia y recursos', icon: 'home', run: () => this.go('Inicio') },
      { label: 'Aplicaciones', hint: 'Tus aplicaciones', icon: 'grid', run: () => this.go('Aplicaciones') },
    ];
    if (this.hasTenencia())
      list.push({
        label: 'Tenencia obligatoria',
        hint: 'Existencias mínimas',
        icon: 'shield',
        run: () => this.openTenencia(),
      });
    if (u.role !== 'User')
      list.push({ label: 'Favoritos', hint: 'Accesos habituales', icon: 'star', run: () => this.go('Favoritos') });
    list.push(
      { label: 'Mi cuenta', hint: 'Configuración', icon: 'user', run: () => this.go('Mi cuenta') },
      {
        label: 'Cambiar mi contraseña',
        hint: 'Mi cuenta',
        icon: 'key',
        run: () => {
          this.go('Mi cuenta');
          this.openSetting('password');
        },
      },
      {
        label: 'Apariencia',
        hint: 'Claro, oscuro o automático',
        icon: 'palette',
        run: () => {
          this.go('Mi cuenta');
          this.openSetting('appearance');
        },
      },
    );
    if (u.canManageUsers)
      list.push(
        { label: 'Usuarios', hint: 'Cuentas y permisos', icon: 'users', run: () => this.go('Usuarios') },
        {
          label: 'Utilidades por usuario',
          hint: 'Habilitar utilidades',
          icon: 'grid',
          run: () => this.go('Utilidades'),
        },
        {
          label: 'Datos de la farmacia',
          hint: 'Provincia y contacto',
          icon: 'home',
          run: () => {
            this.go('Configuración');
            this.openSetting('pharmacy');
          },
        },
        { label: 'Auditoría', hint: 'Registro de operaciones', icon: 'clock', run: () => this.go('Auditoría') },
      );
    list.push({
      label: 'Actualizaciones',
      hint: this.update()?.available ? 'Versión ' + this.update()?.version + ' disponible' : 'Buscar versiones',
      icon: 'download',
      run: () => this.go('Actualizaciones'),
    });
    if (u.canConfigure)
      list.push({
        label: 'Conexión a Nixfarma',
        hint: 'Solo Viteka',
        icon: 'leaf',
        run: () => {
          this.go('Configuración');
          this.openSetting('connection');
        },
      });
    list.push(
      {
        label: 'Portal de soporte',
        hint: 'Crear o consultar solicitud',
        icon: 'user',
        run: () => this.open('support'),
      },
      { label: 'Abrir AnyDesk', hint: 'Ayuda remota', icon: 'monitor', run: () => this.open('anydesk') },
    );
    return list;
  });
  readonly paletteResults = computed<Command[]>(() => {
    const q = this.normalize(this.paletteQuery().trim());
    const found = q
      ? this.commands().filter((c) => this.normalize(c.label + ' ' + c.hint).includes(q))
      : this.commands();
    if (q && this.hasTenencia())
      found.push({
        label: 'Buscar «' + this.paletteQuery().trim() + '» en Tenencia',
        hint: 'Grupo, artículo o código nacional',
        icon: 'search',
        run: () => this.openTenencia(this.paletteQuery().trim()),
      });
    return found;
  });
  readonly paletteActive = computed(() => Math.min(this.paletteIndex(), Math.max(this.paletteResults().length - 1, 0)));
  readonly settingDialog = signal<'password' | 'appearance' | 'startup' | 'connection' | 'pharmacy' | null>(null);
  readonly draftTheme = signal('auto');
  readonly draftStart = signal(false);
  readonly selectedUtility = signal<string | null>(null);
  readonly schemaMessage = signal('');
  readonly schemaBusy = signal(false);
  readonly inSettings = computed(() => SETTINGS_PAGES.includes(this.page()));
  readonly inTenencia = computed(() => this.page() === 'Aplicaciones' && this.selectedUtility() === TENENCIA);
  readonly collapsed = signal(false);
  readonly theme = signal('auto');
  readonly systemDark = signal(matchMedia('(prefers-color-scheme: dark)').matches);
  readonly openFavorites = signal(false);
  readonly resources = [
    { id: 'manuals', icon: 'book', title: 'Manuales Nixfarma', description: 'Manuales y documentación' },
    { id: 'training', icon: 'video', title: 'Vídeos y formación', description: 'Canal de YouTube Viteka' },
    { id: 'website', icon: 'globe', title: 'Web Viteka', description: 'Servicios y novedades' },
  ];
  readonly utilities = signal<Utility[]>([]);
  readonly users = signal<UserView[]>([]);
  readonly remoteReady = signal(true);
  readonly permissionCatalog = signal<Utility[]>([]);
  readonly dialog = signal<'ticket' | 'remote' | null>(null);
  readonly toast = signal('');
  readonly historyOpen = signal(false);
  readonly history = signal<UpdateHistory | null>(null);
  readonly historyBusy = signal(false);
  readonly editorOpen = signal(false);
  readonly resetOpen = signal(false);
  readonly title = computed(() => (this.selectedUtility() ? 'Tenencia obligatoria' : this.page()));
  readonly subtitle = computed(() =>
    this.selectedUtility()
      ? 'Existencias mínimas según el listado de tu provincia.'
      : {
          Inicio: 'Asistencia y recursos de tu farmacia.',
          Aplicaciones: 'Tus aplicaciones autorizadas, en un mismo lugar.',
          Actualizaciones: 'Novedades, versiones y mantenimiento.',
          Favoritos: 'Tus accesos habituales.',
          Auditoría: 'Registro de operaciones de la farmacia.',
          Usuarios: 'Cuentas, roles y acceso a las aplicaciones.',
          Utilidades: 'Habilita las utilidades que puede usar cada cuenta.',
          'Mi cuenta': 'Tu contraseña y preferencias personales.',
          Configuración: 'Tu cuenta, preferencias y opciones disponibles según tu perfil.',
        }[this.page()],
  );
  readonly update = this.bridge.updateNotice;
  readonly checking = signal(false);
  readonly updating = signal(false);
  readonly updateMessage = signal('');
  readonly nixState = signal<NixFarmaState | null>(null);
  readonly nixBusy = signal(false);
  readonly nixMessage = signal('');
  readonly nixResult = signal<NixFarmaResponse | null>(null);
  readonly oracleCodeLabel = computed(() => (this.nixResult()?.oracleCode ?? 0).toString().padStart(5, '0'));
  loginName = '';
  loginPassword = '';
  currentPassword = '';
  newPassword = '';
  repeatPassword = '';
  editId: string | null = null;
  editName = '';
  editRole: Role = 'User';
  editActive = true;
  editPassword = '';
  editRepeat = '';
  editUtilityIds: string[] = [];
  resetId = '';
  resetName = '';
  resetPassword = '';
  resetRepeat = '';
  private toastTimer?: ReturnType<typeof setTimeout>;
  private lastWheel = 0;
  private previousFocus: HTMLElement | null = null;
  constructor() {
    const media = matchMedia('(prefers-color-scheme: dark)');
    const change = () => this.systemDark.set(media.matches);
    media.addEventListener('change', change);
    const wheel = (e: WheelEvent) => {
      if (!e.ctrlKey) return;
      e.preventDefault();
      const now = performance.now();
      if (now - this.lastWheel < 80) return;
      this.lastWheel = now;
      void this.setZoom(this.bridge.zoom() + (e.deltaY < 0 ? 10 : -10));
    };
    document.addEventListener('wheel', wheel, { passive: false });
    const sessionTimer = setInterval(() => {
      if (this.user() && !this.busy()) void this.refreshSession();
    }, 60000);
    inject(DestroyRef).onDestroy(() => {
      media.removeEventListener('change', change);
      document.removeEventListener('wheel', wheel);
      clearTimeout(this.toastTimer);
      clearInterval(sessionTimer);
    });
    effect(() => {
      const dark = this.theme() === 'dark' || (this.theme() === 'auto' && this.systemDark());
      document.documentElement.dataset['theme'] = dark ? 'dark' : 'light';
      void this.bridge.call('window.appearance', { dark }).catch(() => {});
    });
    effect(() => {
      const modal =
        this.settingDialog() ||
        this.dialog() ||
        this.historyOpen() ||
        this.editorOpen() ||
        this.resetOpen() ||
        this.updatePrompt() ||
        this.paletteOpen();
      if (modal) {
        this.previousFocus = document.activeElement as HTMLElement;
        setTimeout(
          () =>
            document
              .querySelector<HTMLElement>(this.paletteOpen() ? '.palette input' : '[role=dialog] button')
              ?.focus(),
          0,
        );
      } else if (this.previousFocus?.isConnected) this.previousFocus.focus();
    });
    effect(() => {
      if (!this.user()) {
        this.loginPassword = '';
        this.currentPassword = '';
        this.newPassword = '';
        this.repeatPassword = '';
        this.editPassword = '';
        this.editRepeat = '';
        this.resetPassword = '';
        this.resetRepeat = '';
        this.selectedUtility.set(null);
        this.schemaMessage.set('');
        this.nixMessage.set('');
        this.nixState.set(null);
        this.nixResult.set(null);
        this.users.set([]);
        this.utilities.set([]);
        this.settingDialog.set(null);
        this.dialog.set(null);
        this.historyOpen.set(false);
        this.editorOpen.set(false);
        this.resetOpen.set(false);
        this.updatePrompt.set(false);
        this.paletteOpen.set(false);
        this.ready.set(false);
        this.favorites.set([]);
      } else {
        if (!this.user()?.canConfigure) {
          this.nixState.set(null);
          this.nixResult.set(null);
        }
        if (!this.user()?.canManageUsers) this.users.set([]);
        if (!this.canGo(this.page())) this.page.set('Inicio');
        if (
          this.selectedUtility() &&
          this.user()?.role === 'User' &&
          !this.user()?.utilityIds.includes(this.selectedUtility()!)
        )
          this.selectedUtility.set(null);
      }
    });
    effect(() => {
      const state = this.update();
      if (state && !this.checking())
        this.updateMessage.set(
          state.message ||
            (state.checking
              ? 'Buscando nuevas versiones…'
              : state.available
                ? `Versión ${state.version} disponible.`
                : ''),
        );
    });
    // Ventana emergente: avisa una vez por versión y sesión de Windows a quien puede instalar en este equipo.
    effect(() => {
      const state = this.update(),
        u = this.user();
      if (!this.ready() || !u || u.mustChangePassword || !state?.available || !state.version) return;
      if (!u.canInstallUpdates || !state.canInstallHere || this.updating() || this.bridge.installing()) return;
      if (sessionStorage.getItem(this.promptKey(state.version)) === '1') return;
      this.updatePrompt.set(true);
    });
    this.tenenciaSummary.set(this.read<TenenciaSummary>('tenencia.summary'));
    void this.initialize();
  }
  readonly phaseLabels: Record<string, string> = {
    preparing: 'Preparando actualización',
    downloading: 'Descargando paquete',
    verifying: 'Verificando integridad',
    handoff: 'Preparando instalación y permiso de Windows',
    installing: 'Instalando; Viteka volverá a abrirse en Inicio',
    cancelled: 'Actualización cancelada',
    failed: 'No se pudo completar la actualización',
  };
  async cancelUpdate() {
    try {
      await this.bridge.call('update.cancel');
    } catch (e) {
      this.error.set(this.message(e));
    }
  }
  private promptKey(version: string) {
    return 'viteka.update-prompt.' + this.user()?.id + '.' + version;
  }
  private read<T>(key: string): T | null {
    try {
      const raw = localStorage.getItem('viteka.' + key);
      return raw ? (JSON.parse(raw) as T) : null;
    } catch {
      return null;
    }
  }
  private store(key: string, value: unknown) {
    try {
      localStorage.setItem('viteka.' + key, JSON.stringify(value));
    } catch {
      /* almacenamiento no disponible */
    }
  }
  private normalize(text: string) {
    return text
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .toLocaleLowerCase('es');
  }
  laterUpdate() {
    const v = this.update()?.version;
    if (v) sessionStorage.setItem(this.promptKey(v), '1');
    this.updatePrompt.set(false);
  }
  async installFromPrompt() {
    const v = this.update()?.version;
    if (v) sessionStorage.setItem(this.promptKey(v), '1');
    this.updatePrompt.set(false);
    this.go('Actualizaciones');
    await this.install();
  }
  viewUpdate() {
    this.laterUpdate();
    this.go('Actualizaciones');
  }
  utilityIcon(id: string) {
    return id === TENENCIA ? 'shield' : 'grid';
  }
  navigate(item: NavItem) {
    if (item.utility === TENENCIA) this.openTenencia();
    else if (item.utility) {
      this.go('Aplicaciones');
      this.openUtility(item.utility);
    } else this.go(item.page);
  }
  isActive(item: NavItem) {
    if (item.utility) return this.inTenencia();
    if (item.page === 'Configuración') return this.inSettings();
    if (item.page === 'Aplicaciones') return this.page() === 'Aplicaciones' && !this.inTenencia();
    return this.page() === item.page;
  }
  openTenencia(search = '') {
    if (!this.hasTenencia()) return;
    this.go('Aplicaciones');
    this.tenenciaSearch.set(search);
    this.openUtility(TENENCIA);
  }
  isFavorite(id: string) {
    return this.favorites().includes(id);
  }
  toggleFavorite(id: string, e?: Event) {
    e?.stopPropagation();
    const next = this.isFavorite(id) ? this.favorites().filter((x) => x !== id) : [...this.favorites(), id];
    this.favorites.set(next);
    this.store('favorites.' + this.user()?.id, next);
    this.notify(
      this.isFavorite(id)
        ? 'Acceso directo añadido a la barra lateral.'
        : 'Acceso directo quitado de la barra lateral.',
    );
  }
  onTenenciaSummary(summary: TenenciaSummary) {
    this.tenenciaSummary.set(summary);
    this.store('tenencia.summary', summary);
  }
  summaryTime(at?: string) {
    if (!at) return '';
    const d = new Date(at),
      today = new Date();
    const time = d.toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' });
    return d.toDateString() === today.toDateString()
      ? 'hoy ' + time
      : d.toLocaleDateString('es-ES', { day: 'numeric', month: 'short' }) + ' ' + time;
  }
  initials(name: string) {
    const parts = name
      .replace(/[^\p{L}\p{N} ]/gu, ' ')
      .trim()
      .split(/\s+/);
    return (
      (parts[0]?.[0] ?? '') + (parts.length > 1 ? (parts[parts.length - 1][0] ?? '') : (parts[0]?.[1] ?? ''))
    ).toUpperCase();
  }
  openPalette() {
    this.paletteQuery.set('');
    this.paletteIndex.set(0);
    this.paletteOpen.set(true);
  }
  runCommand(command?: Command) {
    if (!command) return;
    this.paletteOpen.set(false);
    command.run();
  }
  paletteKey(e: KeyboardEvent) {
    const count = this.paletteResults().length;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      this.paletteIndex.set((this.paletteActive() + (e.key === 'ArrowDown' ? 1 : -1) + count) % Math.max(count, 1));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      this.runCommand(this.paletteResults()[this.paletteActive()]);
    }
  }
  async initialize() {
    await this.loadPreferences();
    await this.refreshSession();
    this.loading.set(false);
  }
  async loadPreferences() {
    try {
      const s = await this.bridge.call<AppState>('app.state');
      this.version.set(s.version);
      this.reviewMode.set(s.reviewMode ?? false);
      this.reviewDeviceId.set(s.reviewDeviceId ?? '');
      this.theme.set(s.theme || 'auto');
      this.collapsed.set(s.sidebarCollapsed ?? false);
      this.openFavorites.set(s.openFavorites);
      this.bridge.zoom.set(s.zoomPercent ?? 100);
      if (!this.bridge.native) document.documentElement.style.setProperty('zoom', String(this.bridge.zoom() / 100));
    } catch {
      this.error.set('No se pudieron leer las preferencias.');
    }
  }
  async refreshSession() {
    try {
      const result = await this.bridge.call<{ user: UserView | null }>('auth.state');
      const before = this.user();
      this.user.set(result.user);
      if (result.user && !result.user.mustChangePassword && (!before || before.mustChangePassword)) {
        await this.afterLogin();
      }
    } catch {
      this.user.set(null);
    }
  }
  async afterLogin() {
    await this.loadPreferences();
    await Promise.all([this.loadUtilities(), this.loadUpdateState()]);
    if (this.user()?.canConfigure) await this.loadNixFarma();
    const id = this.user()?.id;
    this.favorites.set(this.read<string[]>('favorites.' + id) ?? []);
    this.selectedUtility.set(null);
    this.page.set('Inicio');
    this.ready.set(true);
  }
  async login() {
    this.busy.set(true);
    this.error.set('');
    try {
      const result = await this.bridge.call<{ user: UserView }>('auth.login', {
        name: this.loginName,
        password: this.loginPassword,
      });
      this.user.set(result.user);
      this.page.set('Inicio');
      if (!result.user.mustChangePassword) await this.afterLogin();
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.loginPassword = '';
      this.busy.set(false);
    }
  }
  async logout() {
    try {
      await this.bridge.call('auth.logout');
    } finally {
      this.user.set(null);
      this.currentPassword = '';
      this.newPassword = '';
      this.repeatPassword = '';
      this.editPassword = '';
      this.resetPassword = '';
      this.error.set('');
      this.page.set('Inicio');
    }
  }
  async changePassword() {
    if (this.newPassword !== this.repeatPassword) {
      this.error.set('Las contraseñas nuevas no coinciden.');
      return;
    }
    const wasInitial = this.user()?.mustChangePassword;
    this.busy.set(true);
    this.error.set('');
    try {
      const result = await this.bridge.call<{ user: UserView }>('auth.password', {
        currentPassword: this.currentPassword,
        newPassword: this.newPassword,
      });
      this.user.set(result.user);
      this.notify('Contraseña actualizada.');
      if (wasInitial) {
        this.page.set('Inicio');
        await this.afterLogin();
      } else this.closeModals(true);
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.currentPassword = '';
      this.newPassword = '';
      this.repeatPassword = '';
      this.busy.set(false);
    }
  }
  canGo(page: Page) {
    return (
      this.nav().some((item) => item.page === page) ||
      page === 'Mi cuenta' ||
      page === 'Actualizaciones' ||
      ((page === 'Usuarios' || page === 'Utilidades' || page === 'Auditoría') && !!this.user()?.canManageUsers)
    );
  }
  go(page: Page) {
    if (!this.canGo(page)) return;
    this.error.set('');
    this.selectedUtility.set(null);
    this.page.set(page);
    if (page === 'Usuarios' || page === 'Utilidades') void this.loadUsers();
    if (page === 'Actualizaciones') void this.loadUpdateState();
    if (page === 'Configuración' && this.user()?.canConfigure) void this.loadNixFarma();
    if (page === 'Inicio') this.tenenciaSummary.set(this.read<TenenciaSummary>('tenencia.summary'));
  }
  roleName(role: Role) {
    return role === 'SuperAdministrator'
      ? 'Superadministrador'
      : role === 'Administrator'
        ? 'Administrador'
        : 'Usuario';
  }
  canEdit(target: UserView) {
    return !!this.user()?.canManageUsers && (this.user()?.role === 'SuperAdministrator' || target.role === 'User');
  }
  async loadUsers() {
    try {
      const r = await this.bridge.call<{ users: UserView[]; utilities: Utility[]; remoteAccessReady?: boolean }>(
        'users.list',
      );
      this.users.set(r.users);
      this.remoteReady.set(r.remoteAccessReady !== false);
      this.permissionCatalog.set(r.utilities);
    } catch (e) {
      this.error.set(this.message(e));
    }
  }
  openUser(target?: UserView) {
    this.editId = target?.id ?? null;
    this.editName = target?.name ?? '';
    this.editRole = target?.role ?? 'User';
    this.editActive = target?.active ?? true;
    this.editUtilityIds = [...(target?.utilityIds ?? [])];
    this.editPassword = '';
    this.editRepeat = '';
    this.error.set('');
    this.editorOpen.set(true);
  }
  toggleUtility(id: string, enabled: boolean) {
    this.editUtilityIds = enabled
      ? [...new Set([...this.editUtilityIds, id])]
      : this.editUtilityIds.filter((x) => x !== id);
  }
  async saveUser() {
    if (!this.editId && this.editPassword !== this.editRepeat) {
      this.error.set('Las contraseñas no coinciden.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      if (this.editId) {
        const r = await this.bridge.call<{ user: UserView | null }>('users.modify', {
          id: this.editId,
          name: this.editName,
          role: this.editRole,
          active: this.editActive,
          utilityIds: this.editUtilityIds,
        });
        this.user.set(r.user);
      } else
        await this.bridge.call('users.create', {
          name: this.editName,
          role: this.editRole,
          password: this.editPassword,
          utilityIds: this.editUtilityIds,
        });
      this.editorOpen.set(false);
      await this.loadUsers();
      this.notify('Cuenta guardada.');
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.editPassword = '';
      this.editRepeat = '';
      this.busy.set(false);
    }
  }
  hasGrant(target: UserView, id: string) {
    return target.role !== 'User' || target.utilityIds.includes(id);
  }
  readonly grantUserId = signal('');
  readonly grantIds = signal<string[]>([]);
  readonly grantTarget = computed(() => this.users().find((u) => u.id === this.grantUserId()) ?? null);
  selectGrantUser(id: string) {
    this.grantUserId.set(id);
    this.grantIds.set([...(this.users().find((u) => u.id === id)?.utilityIds ?? [])]);
  }
  draftGrant(id: string, enabled: boolean) {
    this.grantIds.update((ids) => (enabled ? [...new Set([...ids, id])] : ids.filter((x) => x !== id)));
  }
  async saveGrants() {
    const target = this.grantTarget();
    if (!target || !this.canEdit(target) || target.role !== 'User' || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const r = await this.bridge.call<{ user: UserView | null }>('users.modify', {
        id: target.id,
        name: target.name,
        role: target.role,
        active: target.active,
        utilityIds: this.grantIds(),
      });
      this.user.set(r.user);
      await this.loadUsers();
      this.selectGrantUser(target.id);
      this.notify('Acceso a aplicaciones guardado para ' + target.name + '. Se han cerrado sus sesiones anteriores.');
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.busy.set(false);
    }
  }
  openReset(target: UserView) {
    if (target.id === this.user()?.id) {
      this.go('Mi cuenta');
      this.openSetting('password');
      return;
    }
    this.resetId = target.id;
    this.resetName = target.name;
    this.resetPassword = '';
    this.resetRepeat = '';
    this.error.set('');
    this.resetOpen.set(true);
  }
  async resetUserPassword() {
    if (this.resetPassword !== this.resetRepeat) {
      this.error.set('Las contraseñas no coinciden.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.bridge.call('users.password', { id: this.resetId, password: this.resetPassword });
      this.resetOpen.set(false);
      await this.loadUsers();
      this.notify('Contraseña restablecida. Se pedirá cambiarla en el próximo acceso.');
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.resetPassword = '';
      this.resetRepeat = '';
      this.busy.set(false);
    }
  }
  openUtility(id: string) {
    if (id === 'tenencia-obligatoria' && this.utilities().some((u) => u.id === id)) {
      this.selectedUtility.set(id);
      this.error.set('');
    }
  }
  async inspectSchema() {
    if (this.schemaBusy() || this.nixBusy() || this.updating()) return;
    this.clearConnectionFeedback();
    this.schemaBusy.set(true);
    this.schemaMessage.set('Comprobando tablas en modo de solo lectura…');
    try {
      const r = await this.bridge.call<{ message: string }>('tenencia.schema');
      this.schemaMessage.set(r.message);
    } catch (e) {
      this.schemaMessage.set(this.message(e));
    } finally {
      this.schemaBusy.set(false);
    }
  }
  async loadUtilities() {
    try {
      const r = await this.bridge.call<{ utilities: Utility[] }>('utilities.list');
      this.utilities.set(r.utilities ?? []);
    } catch (e) {
      this.error.set(this.message(e));
    }
  }
  openSetting(kind: 'password' | 'appearance' | 'startup' | 'connection' | 'pharmacy') {
    if ((kind === 'connection' && !this.user()?.canConfigure) || (kind === 'pharmacy' && !this.user()?.canManageUsers))
      return;
    this.error.set('');
    this.currentPassword = '';
    this.newPassword = '';
    this.repeatPassword = '';
    this.draftTheme.set(this.theme());
    this.draftStart.set(this.openFavorites());
    this.settingDialog.set(kind);
    if (kind === 'connection') {
      if (!this.nixBusy() && !this.schemaBusy()) this.clearConnectionFeedback();
      void this.loadNixFarma();
    }
  }
  async savePreference() {
    this.busy.set(true);
    this.error.set('');
    try {
      if (this.settingDialog() === 'appearance') {
        const theme = this.draftTheme();
        await this.bridge.call('settings.theme', { theme });
        this.theme.set(theme);
      } else {
        const enabled = this.draftStart();
        await this.bridge.call('settings.start', { enabled });
        this.openFavorites.set(enabled);
      }
      this.closeModals(true);
      this.notify('Preferencia guardada.');
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.busy.set(false);
    }
  }
  async setStart(e: Event) {
    const enabled = (e.target as HTMLInputElement).checked;
    try {
      await this.bridge.call('settings.start', { enabled });
      this.openFavorites.set(enabled);
    } catch (e) {
      this.error.set(this.message(e));
    }
  }
  async setTheme(e: Event) {
    const theme = (e.target as HTMLSelectElement).value;
    try {
      await this.bridge.call('settings.theme', { theme });
      this.theme.set(theme);
    } catch (e) {
      this.error.set(this.message(e));
    }
  }
  async toggleSidebar() {
    const value = !this.collapsed();
    this.collapsed.set(value);
    try {
      await this.bridge.call('settings.sidebar', { collapsed: value });
    } catch (e) {
      this.error.set(this.message(e));
    }
  }
  async setZoom(percent: number) {
    const value = Math.max(50, Math.min(200, percent));
    this.bridge.zoom.set(value);
    try {
      const r = await this.bridge.call<{ percent: number }>('window.zoom', { percent: value });
      this.bridge.zoom.set(r.percent);
    } catch {
      this.notify('No se pudo guardar el zoom.', 'warn');
    }
  }
  async open(id: string) {
    try {
      const r = await this.bridge.call<{ opened: boolean }>('resource.open', { id });
      if (id === 'anydesk' && !r.opened) {
        this.notify('No encontramos AnyDesk en sus ubicaciones habituales.', 'warn');
        this.dialog.set('remote');
      }
    } catch (e) {
      this.notify(this.message(e), 'warn');
    }
  }
  async copy(id: string) {
    try {
      await this.bridge.call('clipboard.copy', { id });
      this.notify('Correo copiado.');
    } catch (e) {
      this.notify(this.message(e), 'warn');
    }
  }
  async loadUpdateState() {
    try {
      this.update.set(await this.bridge.call<UpdateState>('update.state'));
    } catch (e) {
      this.updateMessage.set(this.message(e));
    }
  }
  async checkUpdates() {
    this.checking.set(true);
    this.updateMessage.set('Buscando nuevas versiones…');
    try {
      const r = await this.bridge.call<UpdateState>('update.check');
      this.update.set(r);
      this.updateMessage.set(
        r.message ||
          (r.available ? `Versión ${r.version} disponible.` : 'No hay una versión posterior verificable disponible.'),
      );
    } catch (e) {
      this.updateMessage.set(this.message(e));
    } finally {
      this.checking.set(false);
    }
  }
  async install() {
    this.updating.set(true);
    this.bridge.progress.set(null);
    try {
      const r = await this.bridge.call<{ cancelled?: boolean }>('update.install');
      if (r.cancelled) this.updateMessage.set('Instalación cancelada. Puedes seguir usando esta versión.');
    } catch (e) {
      this.updateMessage.set(this.message(e));
    } finally {
      this.updating.set(false);
    }
  }
  async publication() {
    try {
      await this.bridge.call('update.publication');
    } catch (e) {
      this.updateMessage.set(this.message(e));
    }
  }
  async showHistory() {
    this.historyOpen.set(true);
    this.historyBusy.set(true);
    try {
      this.history.set(await this.bridge.call<UpdateHistory>('update.history'));
    } catch (e) {
      this.history.set({ local: [], published: [], message: this.message(e) });
    } finally {
      this.historyBusy.set(false);
    }
  }
  eventName(kind: string) {
    return (
      (
        {
          version_started: 'Versión ejecutada',
          installer_started: 'Instalador iniciado',
          check_succeeded: 'Búsqueda completada',
          check_failed: 'Búsqueda sin conexión',
        } as Record<string, string>
      )[kind] || kind
    );
  }
  formatDate(value?: string) {
    return value ? new Date(value).toLocaleString('es-ES') : 'Todavía no se ha comprobado';
  }
  async loadNixFarma() {
    try {
      const state = await this.bridge.call<NixFarmaState>('nixfarma.status');
      if (typeof state.configured === 'boolean') this.nixState.set(state);
      if (state.message && !this.nixBusy() && !this.schemaBusy()) {
        this.clearConnectionFeedback();
        this.nixMessage.set(state.message);
      }
    } catch (e) {
      if (!this.nixBusy() && !this.schemaBusy()) {
        this.clearConnectionFeedback();
        this.nixMessage.set(this.message(e));
      }
    }
  }
  async connectionAction(action: 'configure' | 'test' | 'forget') {
    if (this.nixBusy() || this.schemaBusy() || this.updating()) return;
    const previous = { message: this.nixMessage(), result: this.nixResult(), schema: this.schemaMessage() };
    this.clearConnectionFeedback();
    this.nixBusy.set(true);
    this.nixMessage.set(action === 'test' ? 'Probando conexión de solo lectura…' : '');
    try {
      const r = await this.bridge.call<NixFarmaResponse>('nixfarma.' + action);
      if (r.state) this.nixState.set(r.state);
      if (r.cancelled) {
        this.nixMessage.set(previous.message);
        this.nixResult.set(previous.result);
        this.schemaMessage.set(previous.schema);
        return;
      }
      if (r.message) this.nixMessage.set(r.message);
      if (action === 'test') this.nixResult.set(r);
      else if (r.state && !r.busy && !r.message) {
        this.nixMessage.set(
          action === 'configure'
            ? 'Conexión guardada. Pulsa Probar conexión para verificarla.'
            : 'Se ha eliminado la conexión guardada en el servidor.',
        );
      }
    } catch (e) {
      this.nixMessage.set(this.message(e));
    } finally {
      this.nixBusy.set(false);
    }
  }
  private clearConnectionFeedback() {
    this.schemaMessage.set('');
    this.nixMessage.set('');
    this.nixResult.set(null);
  }
  notify(message: string, kind: 'ok' | 'warn' = 'ok') {
    this.toastKind.set(kind);
    this.toast.set(message);
    clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => this.toast.set(''), 4000);
  }
  private message(error: unknown) {
    return error instanceof Error ? error.message : 'No se pudo completar la operación.';
  }
  closeModals(force = false) {
    if (this.busy() && !force) return;
    this.currentPassword = '';
    this.newPassword = '';
    this.repeatPassword = '';
    this.error.set('');
    this.settingDialog.set(null);
    this.dialog.set(null);
    this.historyOpen.set(false);
    this.editorOpen.set(false);
    this.resetOpen.set(false);
    this.paletteOpen.set(false);
    if (this.updatePrompt()) this.laterUpdate();
    this.editPassword = '';
    this.editRepeat = '';
    this.resetPassword = '';
    this.resetRepeat = '';
  }
  @HostListener('document:keydown', ['$event']) onKey(e: KeyboardEvent) {
    if (e.ctrlKey && ['+', '=', '-', '0'].includes(e.key)) {
      e.preventDefault();
      void this.setZoom(e.key === '0' ? 100 : this.bridge.zoom() + (e.key === '-' ? -10 : 10));
      return;
    }
    if (e.ctrlKey && e.key.toLowerCase() === 'k' && this.user() && !this.user()?.mustChangePassword) {
      e.preventDefault();
      if (this.paletteOpen()) this.paletteOpen.set(false);
      else this.openPalette();
      return;
    }
    if (e.key === 'Escape') {
      this.closeModals();
      return;
    }
    if (e.key === 'Tab') {
      const dialog = document.querySelector('[role=dialog]');
      if (!dialog) return;
      const controls = [
        ...dialog.querySelectorAll<HTMLElement>(
          'button:not([disabled]),input:not([disabled]),select:not([disabled]),a[href]',
        ),
      ].filter((el) => el.offsetParent !== null);
      const first = controls[0],
        last = controls[controls.length - 1];
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last?.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first?.focus();
      }
    }
  }
}
