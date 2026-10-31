using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Viteka.Desktop;

internal sealed record UpdateHandoff(string Installer,string Sha256,long Size,string Log,int ParentPid,string ReadyFile,string ErrorFile,string Version);
internal static class UpdateCompanion
{
    internal static async Task<string> PrepareAsync(string installer,string hash,long size,string log,string version,CancellationToken ct)
    {
        var destination=Path.Combine(Path.GetDirectoryName(installer)!,"Monitor");Directory.CreateDirectory(destination);
        await Task.Run(()=>{
            foreach(var file in Directory.EnumerateFiles(AppContext.BaseDirectory))File.Copy(file,Path.Combine(destination,Path.GetFileName(file)),true);
            // Self-contained runtime: no separately installed .NET is required by the monitor.
            var runtimes=Path.Combine(AppContext.BaseDirectory,"runtimes");if(Directory.Exists(runtimes))foreach(var file in Directory.EnumerateFiles(runtimes,"*",SearchOption.AllDirectories)){
                var target=Path.Combine(destination,Path.GetRelativePath(AppContext.BaseDirectory,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target,true);
            }
        },ct);
        var manifest=Path.Combine(destination,"handoff.json");var value=new UpdateHandoff(installer,hash,size,log,Environment.ProcessId,Path.Combine(destination,"ready"),Path.Combine(destination,"error"),version);
        await File.WriteAllTextAsync(manifest,JsonSerializer.Serialize(value),ct);
        var start=new ProcessStartInfo(Path.Combine(destination,"Viteka.Desktop.exe")){UseShellExecute=false};start.ArgumentList.Add("--watch-update");start.ArgumentList.Add(manifest);
        using var process=Process.Start(start)??throw new IOException("No se pudo abrir la ventana de instalación.");
        var deadline=DateTimeOffset.UtcNow.AddMinutes(5);
        while(!File.Exists(value.ReadyFile)){
            ct.ThrowIfCancellationRequested();
            if(File.Exists(value.ErrorFile)||process.HasExited)throw new IOException("No se inició la instalación. Puede haberse cancelado el permiso de Windows. La aplicación sigue abierta.");
            if(DateTimeOffset.UtcNow>=deadline){try{process.Kill();}catch(InvalidOperationException){}throw new IOException("No se recibió confirmación del instalador. Puedes volver a intentarlo.");}
            await Task.Delay(250,ct);
        }
        return manifest;
    }
    internal static void Run(string manifest)
    {
        var text=new TextBlock{Text="Preparando instalación…",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,14)};
        var progress=new ProgressBar{IsIndeterminate=true,Height=8};var close=new Button{Content="Cerrar",Visibility=Visibility.Collapsed,Margin=new Thickness(0,18,0,0)};
        var panel=new StackPanel{Margin=new Thickness(28)};panel.Children.Add(new TextBlock{Text="Actualizar Viteka Utilidades",FontSize=21,FontWeight=FontWeights.Bold});panel.Children.Add(text);panel.Children.Add(progress);panel.Children.Add(close);
        var window=new Window{Title="Actualización de Viteka",Width=520,Height=250,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=new SolidColorBrush(Color.FromRgb(250,246,236)),Content=panel};
        bool active=true;window.Closing+=(_,e)=>{if(active)e.Cancel=true;};close.Click+=(_,_)=>window.Close();
        window.Loaded+=async(_,_)=>{
            UpdateHandoff? handoff=null;using var installGate=new Semaphore(1,1,"Global\\"+Viteka.Core.ProductProfile.ShellAppId+"Install");var locked=false;
            try{
                var full=Path.GetFullPath(manifest);if(Path.GetDirectoryName(full)!=Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)||new FileInfo(full).Length>8192)throw new InvalidDataException();
                handoff=JsonSerializer.Deserialize<UpdateHandoff>(await File.ReadAllTextAsync(full))??throw new InvalidDataException();
                if(!installGate.WaitOne(0))throw new IOException("Ya hay una actualización en curso.");locked=true;
                var folder=Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
                if(Path.GetFullPath(handoff.Installer)!=Path.Combine(folder,"Viteka-Setup.exe")||Path.GetFullPath(handoff.Log)!=Path.Combine(folder,"instalacion.log")||handoff.ReadyFile!=Path.Combine(AppContext.BaseDirectory,"ready")||handoff.ErrorFile!=Path.Combine(AppContext.BaseDirectory,"error"))throw new InvalidDataException();
                using(var file=new FileStream(handoff.Installer,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    if(file.Length!=handoff.Size||Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant()!=handoff.Sha256.ToLowerInvariant())throw new InvalidDataException("La verificación del paquete no coincide.");
                    text.Text="Esperando el permiso de Windows. La instalación continuará sin asistente.";
                    // Windows approves the installer; the original client then closes while this copy monitors it.
                    var start=new ProcessStartInfo(handoff.Installer,DesktopUpdates.SilentArguments(handoff.Log)){UseShellExecute=true,Verb="runas"};
                    using var installer=Process.Start(start)??throw new IOException("No se pudo iniciar el instalador.");
                    await File.WriteAllTextAsync(handoff.ReadyFile,"ready");
                    text.Text="Instalando "+handoff.Version+"… El instalador no ofrece un porcentaje medible. Viteka volverá a abrirse al finalizar.";
                    await installer.WaitForExitAsync();
                    if(installer.ExitCode!=0)throw new IOException("La instalación no terminó correctamente (código "+installer.ExitCode+"). Reabre Viteka y consulta el historial y el registro de instalación.");
                }
                text.Text="Instalación finalizada. Viteka se está abriendo en Inicio.";progress.IsIndeterminate=false;progress.Value=100;
            }
            catch(Exception e) when(e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or JsonException or CryptographicException){
                text.Text=e is System.ComponentModel.Win32Exception?"Se canceló el permiso de Windows o no se pudo iniciar el instalador. La instalación no se ha confirmado.":e.Message;
                if(handoff is not null){try{await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory,"error"),"No se confirmó la instalación.");}catch(IOException){}}
                progress.Visibility=Visibility.Collapsed;
            }
            finally{if(locked)installGate.Release();active=false;close.Visibility=Visibility.Visible;}
        };
        Application.Current.MainWindow=window;window.Show();
    }
}
