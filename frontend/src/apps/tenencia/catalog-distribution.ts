import { Component, inject, signal, OnInit, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DesktopBridge } from '../../bridge';
import { toBase64 } from './xlsx';
interface State {
  province: string;
  masterVersion: string;
  localVersion: string;
  legacy: boolean;
  canPrepare: boolean;
  hasTrust: boolean;
  conflicts: { groupId: string; field: string; note: string }[];
  history: { id: number; at: string; version: string }[];
}
@Component({
  selector: 'viteka-catalog-distribution',
  standalone: true,
  imports: [FormsModule],
  template: ` <section class="settings">
    <h2>Distribución y versiones del listado</h2>
    @if (state(); as s) {
      <p>Base Viteka: {{ s.masterVersion }} · Listado local: {{ s.localVersion }}</p>
      <div class="actions">
        <button class="text-button" (click)="check()" [disabled]="busy()">Buscar listados en GitHub</button
        ><button class="primary" (click)="importPackage()" [disabled]="busy() || !s.hasTrust">
          Importar actualización firmada
        </button>
      </div>
      @if (!s.hasTrust) {
        <p>Debe instalarse la clave pública de Viteka en el servidor para verificar las actualizaciones de datos.</p>
      }
      @if (s.canPrepare) {
        <label
          >Nueva versión maestra<input maxlength="40" [(ngModel)]="version" placeholder="Málaga-2026-11-01" /></label
        ><button
          class="primary"
          (click)="prepare()"
          [disabled]="busy() || !version || s.legacy || s.conflicts.length > 0"
        >
          Preparar paquete maestro
        </button>
        <p>
          Se guarda un paquete firmado. No se publica en GitHub. Tras autorizar y publicar, impórtalo también en la
          instalación maestra para avanzar su base.
        </p>
      }
      @if (preview(); as p) {
        <h3>Revisar actualización</h3>
        <p>{{ p.fromVersion }} → {{ p.toVersion }} · {{ p.changes.length }} altas, modificaciones o bajas</p>
        <ul>
          @for (c of p.changes; track c.groupId) {
            <li>
              {{ c.action === 'delete' ? 'Baja' : 'Alta / modificación' }} · {{ c.name || c.groupId }} ·
              {{ c.codes ?? 0 }} referencias
            </li>
          }
        </ul>
        @if (p.missingPrevious && !p.alreadyApplied) {
          <p>Falta instalar una versión anterior.</p>
        }
        <button class="primary" (click)="apply()" [disabled]="busy() || (p.missingPrevious && !p.alreadyApplied)">
          {{ p.alreadyApplied ? 'Ya aplicada' : 'Aplicar actualización' }}
        </button>
      }
      @if (s.conflicts.length) {
        <h3>Personalizaciones por revisar</h3>
        @for (c of s.conflicts; track $index) {
          <p>{{ c.groupId }} · {{ c.field }} · {{ c.note }}</p>
          <button (click)="resolve(c, false)" [disabled]="busy()">Conservar personalización</button>
          @if (c.groupId !== '*') {
            <button (click)="resolve(c, true)" [disabled]="busy()">Usar valor de Viteka</button>
          }
        }
      }
      <details>
        <summary>Recuperar una revisión anterior</summary>
        <p>
          Se conserva la base maestra y el historial de paquetes; la recuperación crea una personalización revisable.
        </p>
        @for (h of s.history; track h.id) {
          <p>
            {{ date(h.at) }} · {{ h.version }} <button (click)="rollback(h.id)" [disabled]="busy()">Recuperar</button>
          </p>
        }
      </details>
    }
    @if (message()) {
      <p role="status">{{ message() }}</p>
    }
    @if (error()) {
      <p class="error" role="alert">{{ error() }}</p>
    }
    @for (u of updates(); track u.url) {
      <p>{{ u.name }} <button (click)="open(u.url)">Descargar paquete</button></p>
    }
  </section>`,
})
export class CatalogDistributionComponent implements OnInit {
  private bridge = inject(DesktopBridge);
  readonly changed = output<void>();
  readonly state = signal<State | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly updates = signal<{ name: string; url: string }[]>([]);
  readonly preview = signal<{
    fromVersion: string;
    toVersion: string;
    missingPrevious: boolean;
    alreadyApplied: boolean;
    changes: { groupId: string; action: string; name?: string; codes?: number }[];
  } | null>(null);
  private package: unknown;
  version = '';
  async ngOnInit() {
    await this.load();
  }
  date(at: string) {
    return new Date(at).toLocaleString('es-ES');
  }
  private async load() {
    this.state.set(await this.bridge.call<State>('catalog.data.state'));
  }
  private async run(task: () => Promise<void>) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await task();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'No se pudo completar la operación.');
    } finally {
      this.busy.set(false);
    }
  }
  async prepare() {
    await this.run(async () => {
      const r = await this.bridge.call<{ package: unknown }>('catalog.data.prepare', { version: this.version });
      const name = 'tenencia-' + this.state()?.province + '-' + this.version.replace(/[^a-zA-Z0-9-]/g, '-') + '.vcat';
      const r2 = await this.bridge.call<{ saved: boolean }>('file.save', {
        name,
        base64: toBase64(new TextEncoder().encode(JSON.stringify(r.package))),
      });
      if (r2.saved) this.message.set('Paquete maestro firmado y guardado. Pendiente de publicación autorizada.');
    });
  }
  async importPackage() {
    await this.run(async () => {
      const r = await this.bridge.call<{ package: unknown | null }>('catalog.file.read');
      if (!r.package) return;
      this.package = r.package;
      this.preview.set(await this.bridge.call('catalog.data.preview', { package: r.package }));
    });
  }
  async apply() {
    await this.run(async () => {
      await this.bridge.call('catalog.data.apply', {
        package: this.package,
        expectedVersion: this.state()?.localVersion,
      });
      await this.load();
      this.preview.set(null);
      this.message.set('Actualización aplicada. Consulta de nuevo las existencias con esta versión.');
      this.changed.emit();
    });
  }
  async resolve(c: { groupId: string; field: string }, useMaster: boolean) {
    await this.run(async () => {
      await this.bridge.call('catalog.data.resolve', { ...c, useMaster, expectedVersion: this.state()?.localVersion });
      await this.load();
      this.changed.emit();
    });
  }
  async rollback(revision: number) {
    if (!confirm('¿Recuperar esta revisión como personalización? La base maestra se conserva.')) return;
    await this.run(async () => {
      await this.bridge.call('catalog.data.rollback', { revision, expectedVersion: this.state()?.localVersion });
      await this.load();
      this.changed.emit();
    });
  }
  async check() {
    await this.run(async () => {
      const r = await this.bridge.call<{ updates: { name: string; url: string }[]; message: string }>(
        'catalog.data.check',
      );
      this.updates.set(r.updates);
      this.message.set(r.message);
    });
  }
  async open(url: string) {
    await this.run(async () => {
      await this.bridge.call('catalog.package.open', { url });
    });
  }
}
