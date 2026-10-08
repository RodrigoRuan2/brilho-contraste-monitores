# Brilho e Contraste dos Monitores

Aplicativo leve para Windows que ajusta **brilho e contraste de monitores externos** por DDC/CI. Cada monitor tem seus próprios controles. O app salva os últimos valores, fica na bandeja e pode iniciar com o Windows.

## Perfis

Escolha **Jogo**, **Trabalho** ou **Noite** na janela. Ajuste brilho e contraste das telas e clique em **Salvar atual**. Depois, **Aplicar** recupera os valores desse perfil. Também é possível salvar e aplicar pelo menu do ícone na bandeja. Os perfis são guardados em `%LOCALAPPDATA%\BrilhoDosMonitores\profiles.json`. O último ajuste aplicado continua sendo restaurado quando o app abre.

## Reconexão automática

Quando o Windows informa que a configuração das telas mudou, um monitor foi reconectado ou o PC voltou da suspensão, o app procura as telas novamente e restaura os últimos valores salvos. Se o monitor ainda não estiver pronto, tenta novamente algumas vezes. O botão **Atualizar** continua disponível para casos em que o Windows não avisa sobre a mudança.

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
