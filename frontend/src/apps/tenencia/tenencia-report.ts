export interface ReportArticle {
  code: string;
  description: string;
  stock: number | null;
  lastPurchase?: string | null;
  lastSale?: string | null;
  expiry?: string | null;
}
/** Output rule independent of the screen: ten references, purchase only, missing dates last. */
export function purchaseCandidates<T extends ReportArticle>(articles: readonly T[]): T[] {
  return [...articles]
    .sort((a, b) => (b.lastPurchase ?? '').localeCompare(a.lastPurchase ?? '') || a.code.localeCompare(b.code))
    .slice(0, 10);
}
/** CSV cells cannot execute formulae in spreadsheet applications. CN strings keep leading zeroes. */
export function csvCell(value: unknown): string {
  let text = String(value ?? '');
  if (/^[=+@\-\t\r]/.test(text)) text = "'" + text;
  return /[;"\r\n]/.test(text) ? '"' + text.replace(/"/g, '""') + '"' : text;
}
