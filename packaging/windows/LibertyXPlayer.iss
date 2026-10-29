; Instalador do LibertyX Player para Windows (Inno Setup 6), compilado pelo CI.
; Instala sem pedir administrador (na pasta do usuário), ou para todos se a pessoa escolher;
; registra o app no "Abrir com" de cada tipo de vídeo e em Aplicativos padrão — o Windows 10/11
; não deixa um app se fazer padrão sozinho: a pessoa escolhe lá.
; O AppId nunca muda: é ele que faz a versão nova atualizar por cima da anterior.
#ifndef Version
  #define Version "1.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

[Setup]
AppId={{6B1C3E52-8F0A-4C7D-9E21-5A47D0C3B8F4}
AppName=LibertyX Player
AppVersion={#Version}
AppVerName=LibertyX Player {#Version}
AppPublisher=LibertyX
AppPublisherURL=https://fxmx182.github.io
AppSupportURL=https://github.com/fxmx182/libertyx-player-desktop
AppUpdatesURL=https://github.com/fxmx182/libertyx-player-desktop/releases/tag/windows-latest
AppCopyright=LibertyX
DefaultDirName={autopf}\LibertyX Player
DefaultGroupName=LibertyX Player
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
OutputBaseFilename=LibertyXPlayer-Setup-arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=LibertyXPlayer-Setup
#endif
MinVersion=10.0.17763
OutputDir=..\..\dist
SetupIconFile=app.ico
UninstallDisplayIcon={app}\LibertyXPlayer.exe
UninstallDisplayName=LibertyX Player
WizardStyle=modern
WizardImageFile=wizard.bmp,wizard-2x.bmp
WizardSmallImageFile=small.bmp,small-2x.bmp
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes
ShowLanguageDialog=auto
VersionInfoVersion={#Version}
VersionInfoProductName=LibertyX Player
VersionInfoDescription=LibertyX Player

[Languages]
; o primeiro é o de quem não fala nenhum dos três (como no app: inglês)
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Messages]
pt.WelcomeLabel2=Isto vai instalar o [name/ver] no seu computador.%n%nSeus vídeos do computador e da rede, com rolagem quadro a quadro, legendas e "continuar assistindo".
en.WelcomeLabel2=This will install [name/ver] on your computer.%n%nYour videos from the computer and the network, with frame-accurate scrubbing, subtitles and "continue watching".
es.WelcomeLabel2=Esto instalará [name/ver] en su equipo.%n%nSus videos del equipo y de la red, con desplazamiento cuadro a cuadro, subtítulos y "seguir viendo".

[CustomMessages]
pt.Video=Vídeo
en.Video=Video
es.Video=Video
pt.Descricao=Player de vídeo com biblioteca, rolagem quadro a quadro e pastas da rede
en.Descricao=Video player with a library, frame-accurate scrubbing and network folders
es.Descricao=Reproductor de video con biblioteca, desplazamiento cuadro a cuadro y carpetas de red

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\..\dist\{#Arch}\LibertyXPlayer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\dist\{#Arch}\libmpv-2.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\dist\{#Arch}\LICENCAS-libmpv.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\LibertyX Player"; Filename: "{app}\LibertyXPlayer.exe"; Comment: "{cm:Descricao}"
Name: "{autodesktop}\LibertyX Player"; Filename: "{app}\LibertyXPlayer.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\LibertyX.Player.Video"; ValueType: string; ValueData: "{cm:Video}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\LibertyX.Player.Video"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "{cm:Video}"
Root: HKA; Subkey: "Software\Classes\LibertyX.Player.Video\DefaultIcon"; ValueType: string; ValueData: "{app}\LibertyXPlayer.exe,0"
Root: HKA; Subkey: "Software\Classes\LibertyX.Player.Video\shell\open\command"; ValueType: string; ValueData: """{app}\LibertyXPlayer.exe"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "LibertyX Player"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\shell\open\command"; ValueType: string; ValueData: """{app}\LibertyXPlayer.exe"" ""%1"""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\LibertyX"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "LibertyX Player"
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "{cm:Descricao}"
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\LibertyXPlayer.exe,0"
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "LibertyX Player"; ValueData: "Software\LibertyX\LibertyX Player\Capabilities"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.mkv\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mkv"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkv"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.mp4\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mp4"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mp4"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.m4v\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".m4v"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m4v"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.avi\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".avi"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".avi"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.mov\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mov"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mov"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.webm\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".webm"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".webm"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.wmv\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".wmv"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wmv"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.flv\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".flv"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".flv"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.mpg\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mpg"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mpg"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.mpeg\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mpeg"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mpeg"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.ts\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".ts"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ts"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.m2ts\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".m2ts"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m2ts"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.mts\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mts"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mts"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.3gp\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".3gp"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".3gp"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.ogv\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".ogv"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ogv"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.vob\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".vob"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".vob"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.divx\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".divx"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".divx"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.rmvb\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".rmvb"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".rmvb"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.rm\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".rm"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".rm"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.asf\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".asf"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".asf"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.f4v\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".f4v"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".f4v"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.mxf\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mxf"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mxf"; ValueData: "LibertyX.Player.Video"
Root: HKA; Subkey: "Software\Classes\.y4m\OpenWithProgids"; ValueType: string; ValueName: "LibertyX.Player.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\LibertyXPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".y4m"; ValueData: ""
Root: HKA; Subkey: "Software\LibertyX\LibertyX Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".y4m"; ValueData: "LibertyX.Player.Video"

[Run]
Filename: "{app}\LibertyXPlayer.exe"; Description: "{cm:LaunchProgram,LibertyX Player}"; Flags: nowait postinstall skipifsilent
