[CmdletBinding()]
param([switch]$InstallerOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try{
 function Invoke-ReviewDotnet([string[]]$Arguments){& dotnet @Arguments;if($LASTEXITCODE -ne 0){throw 'Review build failed.'}}
 if(-not $InstallerOnly){
  & npm --prefix frontend ci --no-audit --no-fund;if($LASTEXITCODE -ne 0){throw 'npm failed'}
  & npm --prefix frontend run build;if($LASTEXITCODE -ne 0){throw 'Angular failed'}
  & python scripts/copy-ui.py;if($LASTEXITCODE -ne 0){throw 'UI copy failed'}
  Invoke-ReviewDotnet -Arguments @('restore','Viteka.Utilidades.slnx','-p:VitekaReview=true','--locked-mode')
  Invoke-ReviewDotnet -Arguments @('build','Viteka.Utilidades.slnx','-c','Release','-p:VitekaReview=true','--no-restore')
  Invoke-ReviewDotnet -Arguments @('run','--project','tests/Viteka.Core.Checks','-c','Release','-p:VitekaReview=true','--no-build')
  foreach($entry in @(@('Viteka.Desktop','Cliente'),@('Viteka.Server','Servidor'))){
   Invoke-ReviewDotnet -Arguments @('publish',('src/'+$entry[0]),'-c','Release','-r','win-x64','--self-contained','true','-p:VitekaReview=true','-p:RestoreLockedMode=true','-o',('artifacts/review/windows-x64/'+$entry[1]))
  }
 }
 $compiler=Get-Command ISCC.exe -ErrorAction SilentlyContinue
 $iscc=if($compiler){$compiler.Source}else{Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'}
 & ./packaging/windows/prepare-webview.ps1
 $version=([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.Version
 & $iscc '/DReviewBuild=1' ('/DAppVersion='+$version) ('/DPublishRoot='+ (Join-Path $root 'artifacts/review/windows-x64')) 'packaging/windows/Viteka.Utilidades.iss'
 if($LASTEXITCODE -ne 0){throw 'Review installer failed.'}
}finally{Pop-Location}
