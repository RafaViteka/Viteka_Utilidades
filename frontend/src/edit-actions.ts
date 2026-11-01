import { Component, input, output } from '@angular/core';

@Component({
  selector: 'viteka-edit-actions',
  standalone: true,
  template: `
    <div class="edit-actions">
      <button type="button" class="cancel" (click)="cancel.emit()" [disabled]="busy()">Cancelar</button>
      <button
        [type]="submit() ? 'submit' : 'button'"
        class="primary"
        [disabled]="busy() || saveDisabled()"
        (click)="saveClick()"
      >{{ busy() ? 'Guardando…' : 'Guardar' }}</button>
    </div>
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .edit-actions { display: flex; justify-content: flex-end; align-items: center; flex-wrap: wrap; gap: 8px; margin-top: 8px; padding-top: 12px; border-top: 1px solid var(--line); }
    button { min-height: 36px; min-width: 92px; padding: 8px 16px; border-radius: 6px; font-size: var(--fs-xs); font-weight: 650; }
    .cancel { background: transparent; border: 1px solid var(--line); color: var(--ink); }
    .cancel:hover:not(:disabled) { background: var(--surface); border-color: var(--hover-line); }
  `],
})
export class EditActionsComponent {
  readonly busy = input(false);
  readonly saveDisabled = input(false);
  readonly submit = input(true);
  readonly cancel = output<void>();
  readonly save = output<void>();
  saveClick() { if (!this.submit()) this.save.emit(); }
}
