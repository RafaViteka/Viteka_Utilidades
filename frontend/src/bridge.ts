import { Injectable, signal } from '@angular/core';
interface NativeMessage { id?: string; ok?: boolean; result?: unknown; error?: string; type?: string; percent?: number }
interface NativeView { postMessage(message: unknown): void; addEventListener(type: 'message', listener: (event: {data: NativeMessage}) => void): void }
declare global { interface Window { chrome?: {webview?: NativeView} } }
export interface AppState {version: string; openFavorites: boolean}
export interface UpdateState {available: boolean; version?: string}
@Injectable({providedIn:'root'})
export class DesktopBridge {
 readonly native=!!window.chrome?.webview; readonly progress=signal<number|null>(null);
 private pending=new Map<string,{resolve:(r:unknown)=>void;reject:(e:Error)=>void;timer:ReturnType<typeof setTimeout>}>();
 constructor(){window.chrome?.webview?.addEventListener('message',event=>{const data=event.data;if(data.type==='update-progress'&&typeof data.percent==='number'){this.progress.set(data.percent);return;}const p=data.id?this.pending.get(data.id):undefined;if(!p)return;clearTimeout(p.timer);this.pending.delete(data.id!);data.ok?p.resolve(data.result):p.reject(new Error(data.error||'No se pudo completar la operación.'));});}
 async call<T>(command:string,payload?:unknown):Promise<T>{
  if(!this.native){if(command==='app.state')return {version:'Vista previa web',openFavorites:localStorage.getItem('viteka.openFavorites')==='true'} as T;if(command==='settings.start'){const enabled=(payload as {enabled:boolean}).enabled;localStorage.setItem('viteka.openFavorites',String(enabled));return {openFavorites:enabled} as T;}throw new Error('Disponible en la aplicación Windows.');}
  const id=crypto.randomUUID();return new Promise<T>((resolve,reject)=>{const timer=setTimeout(()=>{this.pending.delete(id);reject(new Error('La operación ha tardado demasiado.'));},command==='update.install'?660000:30000);this.pending.set(id,{resolve:r=>resolve(r as T),reject,timer});window.chrome!.webview!.postMessage({id,command,payload});});
 }
}
