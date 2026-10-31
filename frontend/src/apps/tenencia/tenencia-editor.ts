import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DesktopBridge } from '../../bridge';
import { CatalogDistributionComponent } from './catalog-distribution';
import { IconComponent } from '../../icon';
import { buildXlsx, Cell, loadLogo, toBase64 } from './xlsx';
import type { TenenciaFields } from './tenencia';

export interface CatalogGroup {
  id: string;
  name: string;
  minimum: number;
  codes: string[];
  descriptions?: Record<string, string> | null;
  atcPrefix?: string | null;
  classificationCode?: string | null;
  selectionNote?: string | null;
}
interface CatalogList {
  version: string;
  source: string;
  groups: CatalogGroup[];
}
interface CatalogResponse {
  list: CatalogList | null;
  customized: boolean;
  provinceName?: string;
}
interface FoundArticle {
  code: string;
  description: string;
  stock: number | null;
  issue?: string | null;
}
type SearchMode = 'code' | 'description' | 'homogeneous' | 'therapeutic.groups' | 'therapeutic.articles';
interface Classification {
  code: string;
  description: string;
  class: string;
  type: string;
  level: number | null;
  parent: string;
  source: string;
}
interface GroupDraft {
  id?: string;
  code: string;
  name: string;
  minimum: number;
  note: string;
  afterId?: string;
}
/** Tamaño de cada envío de artículos (el servicio limita el tamaño de cada petición). */
const CHUNK = 50;

/**
 * Edición del listado de tenencia (solo superadministrador): grupos, mínimos, notas y artículos,
 * con búsqueda en NixFarma por código nacional, descripción o agrupación homogénea, y exportación
 * o importación masiva de códigos.
 */
@Component({
  selector: 'viteka-tenencia-editor',
  standalone: true,
  imports: [FormsModule, IconComponent, CatalogDistributionComponent],
  templateUrl: './tenencia-editor.html',
  styleUrl: './tenencia-editor.css',
})
export class TenenciaEditorComponent implements OnInit {
  private bridge = inject(DesktopBridge);
  readonly fields = input<TenenciaFields | null>(null);
  readonly done = output<boolean>();
  readonly toast = output<string>();
  readonly list = signal<CatalogList | null>(null);
  readonly customized = signal(false);
  readonly provinceName = signal('');
  readonly busy = signal(false);
  readonly error = signal('');
  readonly filterText = signal('');
  readonly chosenId = signal<string | null>(null);
  readonly mode = signal<SearchMode>('code');
  readonly searchText = signal('');
  readonly classifications = signal<Classification[]>([]);
  readonly therapeuticChoice = signal<Classification | null>(null);
  readonly descendants = signal(false);
  chooseClassification(g: Classification) {
    this.therapeuticChoice.set(g);
    this.searchText.set(g.code);
    this.mode.set('therapeutic.articles');
    this.results.set([]);
    this.picked.set([]);
  }
  readonly field = signal('');
  readonly searching = signal(false);
  readonly results = signal<FoundArticle[]>([]);
  readonly searchMessage = signal('');
  readonly picked = signal<string[]>([]);
  readonly draft = signal<GroupDraft | null>(null);
  readonly confirming = signal<{ text: string; label: string; run: () => Promise<void> } | null>(null);
  readonly importing = signal(false);
  readonly importText = signal('');
  readonly importReport = signal('');
  readonly distributionOpen = signal(false);
  async distributionChanged() {
    await this.call('state');
    this.changed = true;
  }
  private changed = false;
  private collator = new Intl.Collator('es', { numeric: true, sensitivity: 'base' });
  readonly groups = computed(() => {
    const q = this.filterText().trim().toLocaleLowerCase('es');
    return (this.list()?.groups ?? []).filter(
      (g) =>
        !q ||
        g.name.toLocaleLowerCase('es').includes(q) ||
        this.code(g).toLocaleLowerCase('es').includes(q) ||
        g.codes.some((c) => c.includes(q)),
    );
  });
  readonly chosen = computed(
    () => this.list()?.groups.find((g) => g.id === this.chosenId()) ?? this.groups().at(0) ?? null,
  );
  readonly chosenArticles = computed(() => {
    const g = this.chosen();
    if (!g) return [];
    return g.codes
      .map((code) => ({ code, description: this.describe(g, code) }))
      .sort((a, b) => this.collator.compare(a.description, b.description));
  });
  readonly totalCodes = computed(() => (this.list()?.groups ?? []).reduce((n, g) => n + g.codes.length, 0));
  readonly homogeneous = computed(() => this.fields()?.homogeneous ?? []);
  readonly addable = computed(() => {
    const g = this.chosen();
    return this.results().filter((a) => g && !g.codes.includes(a.code));
  });
  async ngOnInit() {
    await this.call('state');
    if (this.homogeneous().length) this.field.set(this.homogeneous()[0]);
  }
  code(g: CatalogGroup) {
    return g.classificationCode || g.atcPrefix || '';
  }
  describe(g: CatalogGroup, code: string) {
    return g.descriptions?.[code] || 'CN ' + code;
  }
  private async call(action: string, payload?: unknown) {
    const r = await this.bridge.call<CatalogResponse>(
      'tenencia.catalog.' + action,
      action === 'state' ? {} : { ...((payload as object) ?? {}), expectedVersion: this.list()?.version },
    );
    this.list.set(r.list);
    this.customized.set(r.customized);
    this.provinceName.set(r.provinceName ?? '');
    if (action !== 'state') this.changed = true;
    return r;
  }
  private async guard(task: () => Promise<void>) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await task();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'No se pudo guardar el cambio.');
    } finally {
      this.busy.set(false);
    }
  }
  finish() {
    this.done.emit(this.changed);
  }
  choose(g: CatalogGroup) {
    this.chosenId.set(g.id);
    this.picked.set([]);
  }
  newGroup() {
    this.error.set('');
    this.draft.set({ code: '', name: '', minimum: 1, note: '', afterId: this.chosen()?.id });
    this.focusDialog();
  }
  editGroup(g: CatalogGroup) {
    this.error.set('');
    this.draft.set({ id: g.id, code: this.code(g), name: g.name, minimum: g.minimum, note: g.selectionNote ?? '' });
    this.focusDialog();
  }
  private focusDialog() {
    setTimeout(() => document.querySelector<HTMLInputElement>('.editor-dialog input')?.focus(), 0);
  }
  patchDraft(change: Partial<GroupDraft>) {
    this.draft.update((d) => (d ? { ...d, ...change } : d));
  }
  saveGroup() {
    const d = this.draft();
    if (!d) return;
    return this.guard(async () => {
      const before = new Set((this.list()?.groups ?? []).map((g) => g.id));
      await this.call('group.save', {
        id: d.id,
        name: d.name,
        code: d.code,
        minimum: Math.round(Number(d.minimum)),
        note: d.note,
        afterId: d.afterId,
      });
      const created = this.list()?.groups.find((g) => !before.has(g.id));
      if (created) this.chosenId.set(created.id);
      this.draft.set(null);
      this.toast.emit(d.id ? 'Grupo actualizado.' : 'Grupo añadido.');
    });
  }
  deleteGroup(g: CatalogGroup) {
    this.confirming.set({
      text: `¿Eliminar el grupo «${g.name}» y sus ${g.codes.length} artículos del listado?`,
      label: 'Eliminar grupo',
      run: async () => {
        await this.call('group.delete', { id: g.id });
        if (this.chosenId() === g.id) this.chosenId.set(null);
        this.toast.emit('Grupo eliminado.');
      },
    });
  }
  removeCode(code: string) {
    const g = this.chosen();
    if (!g) return;
    return this.guard(async () => {
      await this.call('codes.remove', { id: g.id, codes: [code] });
      this.toast.emit('Artículo ' + code + ' quitado del grupo.');
    });
  }
  resetList() {
    this.confirming.set({
      text: 'Se descartarán todos los cambios y se volverá al listado provincial original. ¿Continuar?',
      label: 'Restaurar original',
      run: async () => {
        await this.call('reset');
        this.chosenId.set(null);
        this.toast.emit('Listado original restaurado.');
      },
    });
  }
  confirm() {
    const c = this.confirming();
    this.confirming.set(null);
    if (c) return this.guard(c.run);
    return undefined;
  }
  setMode(mode: SearchMode) {
    this.mode.set(mode);
    this.results.set([]);
    this.picked.set([]);
    this.searchMessage.set('');
  }
  search() {
    if (this.searching()) return;
    const text = this.searchText().trim();
    if (!text) return;
    this.searching.set(true);
    this.error.set('');
    this.searchMessage.set('');
    void this.bridge
      .call<{ code: string; message: string; articles: FoundArticle[]; classifications?: Classification[] }>(
        'tenencia.catalog.search',
        {
          mode: this.mode(),
          text,
          field:
            this.mode() === 'homogeneous'
              ? this.field()
              : this.mode() === 'therapeutic.articles'
                ? JSON.stringify({ ...this.therapeuticChoice(), descendants: this.descendants() })
                : '',
        },
      )
      .then((r) => {
        this.results.set(r.articles ?? []);
        this.classifications.set(r.classifications ?? []);
        this.searchMessage.set(r.message);
        const g = this.chosen();
        this.picked.set(
          (r.articles ?? [])
            .filter((a) => g && !g.codes.includes(a.code) && a.issue !== 'not_found')
            .map((a) => a.code),
        );
      })
      .catch((e) => this.error.set(e instanceof Error ? e.message : 'No se pudo buscar en Nixfarma.'))
      .finally(() => this.searching.set(false));
  }
  isPicked(code: string) {
    return this.picked().includes(code);
  }
  togglePick(code: string, on: boolean) {
    this.picked.update((p) => (on ? [...new Set([...p, code])] : p.filter((x) => x !== code)));
  }
  pickAll(on: boolean) {
    this.picked.set(on ? this.addable().map((a) => a.code) : []);
  }
  addPicked() {
    const g = this.chosen();
    if (!g) return;
    const items = this.results()
      .filter((a) => this.isPicked(a.code) && !g.codes.includes(a.code))
      .map((a) => ({ code: a.code, description: a.issue === 'not_found' ? '' : a.description }));
    if (!items.length) return;
    return this.guard(async () => {
      await this.addItems(g.id, items);
      this.picked.set([]);
      this.toast.emit(items.length + (items.length === 1 ? ' artículo añadido.' : ' artículos añadidos.'));
    });
  }
  private async addItems(id: string, items: { code: string; description: string }[]) {
    for (let i = 0; i < items.length; i += CHUNK)
      await this.call('codes.add', { id, items: items.slice(i, i + CHUNK) });
  }
  stock(n: number | null) {
    return n === null ? '—' : new Intl.NumberFormat('es-ES', { maximumFractionDigits: 3 }).format(n);
  }
  // ——— Exportación e importación masiva de códigos ———
  private rows() {
    const out: (string | number)[][] = [];
    for (const g of this.list()?.groups ?? []) {
      if (!g.codes.length) out.push([this.code(g), g.name, g.minimum, '', '', g.selectionNote ?? '']);
      for (const c of g.codes)
        out.push([this.code(g), g.name, g.minimum, c, g.descriptions?.[c] ?? '', g.selectionNote ?? '']);
    }
    return out;
  }
  private readonly header = ['Código grupo', 'Grupo', 'Mínimo', 'CN', 'Descripción', 'Nota'];
  private day() {
    return new Date().toISOString().slice(0, 10);
  }
  exportCodes(kind: 'xlsx' | 'csv') {
    return this.guard(async () => {
      let bytes: Uint8Array;
      if (kind === 'csv') {
        const cell = (v: unknown) => {
          const t = String(v ?? '');
          return /[;"\n]/.test(t) ? '"' + t.replace(/"/g, '""') + '"' : t;
        };
        const text = '\ufeff' + [this.header, ...this.rows()].map((r) => r.map(cell).join(';')).join('\r\n');
        bytes = new TextEncoder().encode(text);
      } else {
        const logo = await loadLogo(48);
        const rows: Cell[][] = [
          [],
          [],
          [],
          [{ v: 'Tenencia obligatoria · listado de códigos', s: 'title' }],
          [
            {
              v: `${this.provinceName()} · ${this.list()?.groups.length ?? 0} grupos · ${this.totalCodes()} códigos · versión ${this.list()?.version ?? ''}`,
              s: 'muted',
            },
          ],
          [{ v: 'Puedes editar este archivo y volver a importarlo en Editar listado → Importar códigos.', s: 'muted' }],
          [],
          this.header.map((v) => ({ v, s: 'header' as const })),
          ...this.rows().map((r) => [
            r[0],
            { v: r[1], s: 'wrap' as const },
            { v: r[2], s: 'center' as const },
            { v: r[3], s: 'center' as const },
            { v: r[4], s: 'wrap' as const },
            r[5],
          ]),
        ];
        bytes = buildXlsx(
          [
            {
              name: 'Listado',
              widths: [13, 46, 9, 10, 48, 30],
              rows,
              merges: ['A4:F4', 'A5:F5', 'A6:F6'],
              freeze: 8,
              filter: `A8:F${rows.length}`,
              image: logo ?? undefined,
            },
          ],
          'Tenencia obligatoria · códigos',
        );
      }
      const r = await this.bridge.call<{ saved: boolean; file?: string }>('file.save', {
        name: `tenencia-codigos-${this.day()}.${kind}`,
        base64: toBase64(bytes),
      });
      if (r.saved) this.toast.emit('Guardado: ' + (r.file || 'listado'));
    });
  }
  openImport() {
    this.importText.set('');
    this.importReport.set('');
    this.importing.set(true);
    setTimeout(() => document.querySelector<HTMLTextAreaElement>('.import-dialog textarea')?.focus(), 0);
  }
  /**
   * Importación masiva. Admite el formato exportado (Código grupo; Grupo; Mínimo; CN; Descripción)
   * o líneas cortas «grupo; CN; descripción», donde grupo es su código o su nombre.
   */
  runImport() {
    return this.guard(async () => {
      const groups = () => this.list()?.groups ?? [];
      const find = (ref: string, name = '') => {
        const r = ref.trim().toUpperCase(),
          n = (name || ref).trim().toLocaleLowerCase('es');
        return (
          groups().find(
            (g) => r && this.code(g).toUpperCase() === r && (!name || g.name.toLocaleLowerCase('es') === n),
          ) ??
          groups().find((g) => r && this.code(g).toUpperCase() === r) ??
          groups().find((g) => g.name.toLocaleLowerCase('es') === n)
        );
      };
      const pending = new Map<string, { code: string; description: string }[]>();
      let skipped = 0,
        created = 0;
      for (const raw of this.importText().split(/\r?\n/)) {
        const line = raw.trim();
        if (!line) continue;
        const cells = line.split(/[;\t]/).map((c) => c.trim().replace(/^"|"$/g, ''));
        const long = cells.length >= 4;
        const [ref, name, minimum, cn, description] = long ? cells : [cells[0], '', '', cells[1] ?? '', cells[2] ?? ''];
        if (/^c[oó]digo/i.test(ref) && !/^\d/.test(cn)) continue;
        let group = find(ref, name);
        if (!group && long && name) {
          const before = new Set(groups().map((g) => g.id));
          await this.call('group.save', {
            name,
            code: ref,
            minimum: Math.max(1, parseInt(minimum, 10) || 1),
            note: '',
          });
          group = groups().find((g) => !before.has(g.id));
          created++;
        }
        if (!group || (cn && !/^\d{6,7}$/.test(cn))) {
          skipped++;
          continue;
        }
        if (!cn) continue;
        const items = pending.get(group.id) ?? [];
        if (!group.codes.includes(cn) && !items.some((i) => i.code === cn))
          items.push({ code: cn, description: description ?? '' });
        pending.set(group.id, items);
      }
      let added = 0;
      for (const [id, items] of pending) {
        if (!items.length) continue;
        await this.addItems(id, items);
        added += items.length;
      }
      const touched = [...pending.values()].filter((i) => i.length).length;
      this.importReport.set(
        `${added} códigos añadidos en ${touched} grupos` +
          (created ? ` · ${created} grupos nuevos` : '') +
          (skipped ? ` · ${skipped} líneas no reconocidas` : '') +
          '.',
      );
      if (added || created) this.toast.emit('Importación completada.');
    });
  }
}
