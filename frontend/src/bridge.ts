import { Injectable, signal } from '@angular/core';
interface NativeMessage { id?: string; ok?: boolean; result?: unknown; error?: string; type?: string; percent?: number }
interface NativeView { postMessage(message: unknown): void; addEventListener(type: 'message', listener: (event: {data: NativeMessage}) => void): void }
declare global { interface Window { chrome?: {webview?: NativeView} } }
export interface AppState {version: string; openFavorites: boolean; sidebarCollapsed?: boolean; theme?: string; zoomPercent?: number}
export interface NixFarmaState {configured:boolean;saved:boolean;dsn:string;user:string;dsnAvailable:boolean;message?:string}
export interface NixFarmaResponse {state?:NixFarmaState;cancelled?:boolean;busy?:boolean;success?:boolean;code?:string;message?:string;sqlState?:string;oracleCode?:number;checkedAt?:string}
export interface UpdateState {available: boolean; version?: string}
@Injectable({providedIn:'root'})
export class DesktopBridge {
 readonly zoom=signal(100);readonly native=!!window.chrome?.webview; readonly progress=signal<number|null>(null);
 private pending=new Map<string,{resolve:(r:unknown)=>void;reject:(e:Error)=>void;timer:ReturnType<typeof setTimeout>}>();
 constructor(){window.chrome?.webview?.addEventListener('message',event=>{const data=event.data;if(data.type==='zoom-changed'&&typeof data.percent==='number'){this.zoom.set(data.percent);return;}if(data.type==='update-progress'&&typeof data.percent==='number'){this.progress.set(data.percent);return;}const p=data.id?this.pending.get(data.id):undefined;if(!p)return;clearTimeout(p.timer);this.pending.delete(data.id!);data.ok?p.resolve(data.result):p.reject(new Error(data.error||'No se pudo completar la operación.'));});}
 async call<T>(command:string,payload?:unknown):Promise<T>{
  if(!this.native){if(command==='nixfarma.status')return {configured:false,saved:false,dsn:'NIXFARMA',user:'',dsnAvailable:false} as T;if(command==='app.state')return {version:'Vista previa web',openFavorites:localStorage.getItem('viteka.openFavorites')==='true',sidebarCollapsed:localStorage.getItem('viteka.sidebar')==='true',theme:localStorage.getItem('viteka.theme')||'auto',zoomPercent:Math.min(200,Math.max(50,Number(localStorage.getItem('viteka.zoom'))||100))} as T;if(command==='settings.start'){const enabled=(payload as {enabled:boolean}).enabled;localStorage.setItem('viteka.openFavorites',String(enabled));return {openFavorites:enabled} as T;}if(command==='settings.sidebar'){const value=(payload as {collapsed:boolean}).collapsed;localStorage.setItem('viteka.sidebar',String(value));return {} as T;}
  if(command==='settings.theme'){localStorage.setItem('viteka.theme',(payload as {theme:string}).theme);return {} as T;}
  if(command==='window.zoom'){const percent=Math.max(50,Math.min(200,(payload as {percent:number}).percent));localStorage.setItem('viteka.zoom',String(percent));document.documentElement.style.setProperty('zoom',String(percent/100));this.zoom.set(percent);return {percent} as T;}if(command==='window.appearance')return {} as T;
  if(command==='resource.open'){const id=(payload as {id:string}).id;const targets:Record<string,string>={support:'https://soporte.viteka.es/',manuals:'https://wiki.nixfarma.es/',training:'https://www.youtube.com/@viteka_formacion',website:'https://viteka.es/',phone:'tel:+34951552730','support-email':'mailto:soporte@viteka.es','info-email':'mailto:info@viteka.es','anydesk-download':'https://anydesk.com/es/downloads/windows'};if(id==='anydesk')return {opened:false} as T;if(targets[id]){window.open(targets[id],'_blank','noopener,noreferrer');return {opened:true} as T;}}
  if(command==='clipboard.copy'){const id=(payload as {id:string}).id;const email=id==='support-email'?'soporte@viteka.es':id==='info-email'?'info@viteka.es':null;if(email){await navigator.clipboard.writeText(email);return {} as T;}}
  throw new Error('Disponible en la aplicación Windows.');}
  const id=crypto.randomUUID();return new Promise<T>((resolve,reject)=>{const timer=setTimeout(()=>{this.pending.delete(id);reject(new Error('La operación ha tardado demasiado.'));},command==='update.install'?660000:command==='nixfarma.configure'||command==='nixfarma.forget'?600000:command==='nixfarma.test'?45000:30000);this.pending.set(id,{resolve:r=>resolve(r as T),reject,timer});window.chrome!.webview!.postMessage({id,command,payload});});
 }
}
