import { Injectable, signal } from '@angular/core';
interface NativeMessage {
  id?: string;
  ok?: boolean;
  result?: unknown;
  error?: string;
  type?: string;
  percent?: number;
  phase?: string;
  state?: UpdateState;
}
interface NativeView {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: { data: NativeMessage }) => void): void;
}
declare global {
  interface Window {
    chrome?: { webview?: NativeView };
  }
}
export type Role = 'User' | 'Administrator' | 'SuperAdministrator';
export interface UserView {
  id: string;
  name: string;
  role: Role;
  active: boolean;
  mustChangePassword: boolean;
  bootstrapPassword?: boolean;
  utilityIds: string[];
  canConfigure: boolean;
  canManageUsers: boolean;
  canInstallUpdates: boolean;
}
export interface AppState {
  reviewMode?: boolean;
  reviewDeviceId?: string;
  version: string;
  openFavorites: boolean;
  sidebarCollapsed?: boolean;
  theme?: string;
  zoomPercent?: number;
}
export interface NixFarmaState {
  configured: boolean;
  saved: boolean;
  dsn: string;
  user: string;
  dsnAvailable: boolean;
  message?: string;
}
export interface NixFarmaResponse {
  state?: NixFarmaState;
  cancelled?: boolean;
  busy?: boolean;
  success?: boolean;
  code?: string;
  message?: string;
  sqlState?: string;
  oracleCode?: number;
  checkedAt?: string;
}
export interface UpdateState {
  checking?: boolean;
  canInstallHere?: boolean;
  available: boolean;
  version?: string;
  lastChecked?: string;
  lastSuccess?: string;
  message?: string;
}
export interface UpdateHistory {
  local: { at: string; kind: string; version: string }[];
  published: { version: string; title: string; notes: string; publishedAt?: string }[];
  message?: string;
}
export interface Utility {
  id: string;
  name: string;
  description?: string;
}
@Injectable({ providedIn: 'root' })
export class DesktopBridge {
  readonly native = !!window.chrome?.webview;
  readonly zoom = signal(100);
  readonly progress = signal<number | null>(null);
  readonly updatePhase = signal('');
  /** El instalador silencioso se ha lanzado; Viteka se cerrará y volverá a abrirse con la sesión iniciada. */
  readonly installing = signal(false);
  readonly user = signal<UserView | null>(null);
  readonly updateNotice = signal<UpdateState | null>(null);
  private pending = new Map<
    string,
    { resolve: (r: unknown) => void; reject: (e: Error) => void; timer: ReturnType<typeof setTimeout> }
  >();
  constructor() {
    window.chrome?.webview?.addEventListener('message', (event) => {
      const d = event.data;
      if (d.type === 'session-ended') {
        this.user.set(null);
        return;
      }
      if (d.type === 'update-state' && d.state) {
        this.updateNotice.set(d.state);
        return;
      }
      if (d.type === 'zoom-changed' && typeof d.percent === 'number') {
        this.zoom.set(d.percent);
        return;
      }
      if (d.type === 'update-phase' && d.phase) {
        this.updatePhase.set(d.phase);
        this.progress.set(d.percent ?? null);
        return;
      }
      if (d.type === 'update-installing') {
        this.updatePhase.set('installing');
        this.installing.set(true);
        return;
      }
      if (d.type === 'update-progress' && typeof d.percent === 'number') {
        this.progress.set(d.percent);
        return;
      }
      const p = d.id ? this.pending.get(d.id) : undefined;
      if (!p) return;
      clearTimeout(p.timer);
      this.pending.delete(d.id!);
      if (!d.ok && d.error === 'AUTH_REQUIRED') this.user.set(null);
      d.ok
        ? p.resolve(d.result)
        : p.reject(
            new Error(
              d.error === 'AUTH_REQUIRED' ? 'Inicia sesión de nuevo.' : d.error || 'No se pudo completar la operación.',
            ),
          );
    });
  }
  async call<T>(command: string, payload?: unknown): Promise<T> {
    if (!this.native) {
      if (command === 'auth.state') return { user: null } as T;
      if (command === 'app.state')
        return {
          version: 'Vista previa web',
          openFavorites: false,
          sidebarCollapsed: false,
          theme: localStorage.getItem('viteka.theme') || 'auto',
          zoomPercent: Number(localStorage.getItem('viteka.zoom')) || 100,
        } as T;
      if (command === 'window.appearance') return {} as T;
      if (command === 'window.zoom') {
        const percent = Math.max(50, Math.min(200, (payload as { percent: number }).percent));
        localStorage.setItem('viteka.zoom', String(percent));
        document.documentElement.style.setProperty('zoom', String(percent / 100));
        this.zoom.set(percent);
        return { percent } as T;
      }
      throw new Error('El acceso con contraseña está disponible en la aplicación Windows.');
    }
    const id = crypto.randomUUID();
    return new Promise<T>((resolve, reject) => {
      const timeout =
        command === 'update.install'
          ? 660000
          : command === 'nixfarma.configure' || command === 'nixfarma.forget'
            ? 600000
            : command === 'nixfarma.test' || command.startsWith('tenencia.')
              ? 45000
              : 30000;
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error('La operación ha tardado demasiado.'));
      }, timeout);
      this.pending.set(id, { resolve: (r) => resolve(r as T), reject, timer });
      window.chrome!.webview!.postMessage({ id, command, payload });
    });
  }
}
