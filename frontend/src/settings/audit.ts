import { Component, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DesktopBridge } from '../bridge';
interface Entry {
  id: number;
  at: string;
  userId: string;
  userName: string;
  utility: string;
  action: string;
  result: string;
  note: string;
}
@Component({
  selector: 'viteka-audit',
  standalone: true,
  imports: [FormsModule],
  template: ` <section class="settings">
    <h2>Auditoría de la farmacia</h2>
    <div class="audit-filters">
      <label>Desde<input type="date" [(ngModel)]="from" /></label
      ><label>Hasta<input type="date" [(ngModel)]="to" /></label>
      <label
        >Usuario<select [(ngModel)]="user">
          <option value="">Todos</option>
          @for (u of users(); track u.id) {
            <option [value]="u.id">{{ u.name }}</option>
          }
        </select></label
      >
      <label
        >Utilidad<select [(ngModel)]="utility">
          <option value="">Todas</option>
          <option value="tenencia-obligatoria">Tenencia obligatoria</option>
        </select></label
      >
      <label>Acción<input [(ngModel)]="action" placeholder="Ej.: tenencia.query" /></label
      ><label
        >Resultado<select aria-label="Resultado" [(ngModel)]="result">
          <option value="">Todos</option>
          <option value="ok">Correcto</option>
          <option value="error">Error</option>
          <option value="pending">En curso o interrumpida</option>
        </select></label
      >
      <button class="primary" (click)="load()" [disabled]="busy()">Filtrar</button>
    </div>
    @if (error()) {
      <p role="alert" class="error">{{ error() }}</p>
    }
    <div class="grant-table">
      <table>
        <thead>
          <tr>
            <th>Fecha y hora</th>
            <th>Usuario</th>
            <th>Acción</th>
            <th>Resultado</th>
            <th>Anotación</th>
          </tr>
        </thead>
        <tbody>
          @for (e of rows(); track e.id) {
            <tr>
              <td>{{ date(e.at) }}</td>
              <td>{{ e.userName }}</td>
              <td>{{ e.action }}</td>
              <td>
                {{ e.result === 'ok' ? 'Correcto' : e.result === 'pending' ? 'En curso o interrumpida' : 'Error' }}
              </td>
              <td>{{ e.note }}</td>
            </tr>
          } @empty {
            <tr>
              <td colspan="5">No hay registros para estos filtros.</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
    @if (next()) {
      <button class="text-button" (click)="load(true)" [disabled]="busy()">Cargar 50 anteriores</button>
    }
    @if (bridge.user()?.canConfigure) {
      <label
        >Conservación (meses)<input
          aria-label="Meses de conservación"
          type="number"
          min="1"
          max="120"
          [ngModel]="retention()"
          (ngModelChange)="retention.set($event)" /></label
      ><button class="text-button" (click)="saveRetention()">Guardar conservación</button>
    }
    <p class="hint">
      Actividad central de los puestos autorizados. Las solicitudes de impresión no acreditan impresión física.
    </p>
  </section>`,
  styles: [
    `
      .audit-filters {
        display: flex;
        gap: 12px;
        flex-wrap: wrap;
        align-items: end;
        margin-bottom: 16px;
      }
      .audit-filters label {
        display: grid;
        gap: 6px;
      }
      td,
      th {
        padding: 9px;
        text-align: left;
      }
    `,
  ],
})
export class AuditComponent implements OnInit {
  readonly bridge = inject(DesktopBridge);
  readonly rows = signal<Entry[]>([]);
  readonly users = signal<{ id: string; name: string }[]>([]);
  readonly next = signal<number | null>(null);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly retention = signal(12);
  async saveRetention() {
    try {
      const r = await this.bridge.call<{ months: number }>('audit.settings', { months: this.retention() });
      this.retention.set(r.months);
      await this.load();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'No se pudo guardar.');
    }
  }
  from = '';
  to = '';
  user = '';
  utility = '';
  action = '';
  result = '';
  async ngOnInit() {
    try {
      const u = await this.bridge.call<{ users: { id: string; name: string }[] }>('users.list');
      this.users.set(u.users);
    } catch {}
    try {
      const r = await this.bridge.call<{ months: number }>('audit.settings');
      this.retention.set(r.months);
    } catch {}
    await this.load();
  }
  date(at: string) {
    return new Date(at).toLocaleString('es-ES');
  }
  async load(more = false) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const r = await this.bridge.call<{ rows: Entry[]; next: number | null }>('audit.query', {
        from: this.from ? new Date(this.from + 'T00:00:00').toISOString() : null,
        to: this.to ? new Date(this.to + 'T23:59:59.999').toISOString() : null,
        user: this.user,
        utility: this.utility,
        action: this.action,
        result: this.result,
        before: more ? this.next() : null,
      });
      this.rows.set(more ? [...this.rows(), ...r.rows] : r.rows);
      this.next.set(r.next);
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'No se pudo consultar la auditoría.');
    } finally {
      this.busy.set(false);
    }
  }
}
