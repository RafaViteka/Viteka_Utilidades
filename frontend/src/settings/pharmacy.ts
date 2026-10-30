import { Component, inject, signal, output, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DesktopBridge } from '../bridge';
export interface PharmacyProfile {
  name: string;
  province: string;
  town: string;
  email: string;
  phone: string;
  address: string;
  postcode: string;
}
@Component({
  selector: 'viteka-pharmacy-settings',
  standalone: true,
  imports: [FormsModule],
  template: ` <p>
      Estos datos se comparten con todos los puestos. La provincia determina la lista de tenencia que se comprobará.
    </p>
    <form class="account-form" (ngSubmit)="save()">
      <label
        >Nombre de la farmacia<input name="pharmacyName" [(ngModel)]="profile.name" maxlength="200" required
      /></label>
      <div class="pharmacy-fields">
        <label
          >Provincia<select aria-label="Provincia" name="province" [(ngModel)]="profile.province" required>
            <option value="">Selecciona provincia</option>
            @for (p of provinces(); track p.code) {
              <option [value]="p.code">{{ p.name }}</option>
            }
          </select></label
        ><label>Localidad<input name="town" [(ngModel)]="profile.town" maxlength="200" required /></label>
      </div>
      <label>Dirección<input name="address" [(ngModel)]="profile.address" maxlength="200" /></label>
      <div class="pharmacy-fields">
        <label
          >Código postal<input
            name="postcode"
            [(ngModel)]="profile.postcode"
            maxlength="5"
            inputmode="numeric" /></label
        ><label>Teléfono<input name="phone" [(ngModel)]="profile.phone" maxlength="40" type="tel" /></label>
      </div>
      <label>Correo electrónico<input name="email" [(ngModel)]="profile.email" maxlength="200" type="email" /></label>
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
      @if (message()) {
        <p role="status">{{ message() }}</p>
      }
      <div class="actions">
        <button type="button" class="text-button" (click)="cancel.emit()" [disabled]="busy()">Cancelar</button
        ><button type="submit" class="primary" [disabled]="busy() || loading()">
          {{ busy() ? 'Guardando…' : 'Guardar datos' }}
        </button>
      </div>
    </form>`,
  styles: [
    `
      .pharmacy-fields {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 16px;
      }
      .pharmacy-fields label {
        display: grid;
        gap: 7px;
        font-size: 13px;
      }
      @media (max-width: 600px) {
        .pharmacy-fields {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
})
export class PharmacySettingsComponent implements OnInit {
  private bridge = inject(DesktopBridge);
  readonly saved = output<void>();
  readonly cancel = output<void>();
  readonly working = output<boolean>();
  readonly busy = signal(false);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly message = signal('');
  readonly provinces = signal<{ code: string; name: string }[]>([]);
  profile: PharmacyProfile = { name: '', province: '', town: '', email: '', phone: '', address: '', postcode: '' };
  async ngOnInit() {
    try {
      const r = await this.bridge.call<{
        profile: PharmacyProfile | null;
        provinces: { code: string; name: string }[];
      }>('pharmacy.state');
      this.provinces.set(r.provinces);
      if (r.profile) this.profile = { ...r.profile };
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'No se pudieron cargar los datos.');
    } finally {
      this.loading.set(false);
    }
  }
  async save() {
    this.busy.set(true);
    this.working.emit(true);
    this.error.set('');
    this.message.set('');
    try {
      const r = await this.bridge.call<{ profile: PharmacyProfile }>('pharmacy.save', this.profile);
      this.profile = { ...r.profile };
      this.message.set('Datos guardados para toda la farmacia. La próxima consulta utilizará esta provincia.');
      this.saved.emit();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'No se pudieron guardar los datos.');
    } finally {
      this.busy.set(false);
      this.working.emit(false);
    }
  }
}
