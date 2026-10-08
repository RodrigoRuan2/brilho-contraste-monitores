# Brilho e Contraste dos Monitores

Aplicativo leve para Windows que ajusta **brilho e contraste de monitores externos** por DDC/CI. Cada monitor tem seus próprios controles. O app salva os últimos valores, fica na bandeja e pode iniciar com o Windows.

## Baixar

[Baixar BrilhoDosMonitores.exe](https://github.com/RodrigoRuan2/brilho-contraste-monitores/releases/latest/download/BrilhoDosMonitores.exe)

Abra o executável. Para manter o app na bandeja, feche a janela pelo **X**. Clique duas vezes no ícone da bandeja para reabrir, ou clique com o botão direito e escolha **Sair** para encerrar.

## Iniciar com o Windows

No menu do ícone da bandeja, marque **Iniciar com o Windows**. O app copia o executável para uma pasta estável do usuário e passa a abrir diretamente na bandeja ao entrar no Windows. Desmarque a opção no mesmo menu para desativar a inicialização automática.

## Requisitos

- Windows com .NET Framework 4.8.
- Monitor externo com controle DDC/CI de brilho e contraste habilitado no menu do monitor.

Os valores ficam em `%LOCALAPPDATA%\BrilhoDosMonitores\settings.json`. Ao abrir, o app reaplica os últimos valores salvos. Nenhuma conexão com a internet é necessária para usar o app.

## Compilar

Em um PowerShell no Windows, execute `./build.ps1`. O script usa o compilador do .NET Framework instalado no sistema e gera `dist/BrilhoDosMonitores.exe`.

O código-fonte principal está em `BrilhoDosMonitores.cs`.
