import * as path from 'node:path';
import * as vscode from 'vscode';
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  TransportKind,
} from 'vscode-languageclient/node';

let client: LanguageClient | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  if (!vscode.workspace.getConfiguration('square.languageServer').get<boolean>('enabled', true)) {
    return;
  }

  const serverPath = vscode.workspace.getConfiguration('square.languageServer').get<string>('path', '');
  const serverArgs = vscode.workspace.getConfiguration('square.languageServer').get<string[]>('args', []);
  const server = createServerOptions(context, serverPath, serverArgs);
  const clientOptions: LanguageClientOptions = {
    documentSelector: [
      { scheme: 'file', language: 'sqx' },
      { scheme: 'file', language: 'sqv' },
    ],
    synchronize: {
      configurationSection: 'square.languageServer',
    },
  };

  client = new LanguageClient(
    'squareLanguageServer',
    'Square Language Server',
    server,
    clientOptions,
  );
  context.subscriptions.push(client);
  await client.start();
}

export async function deactivate(): Promise<void> {
  if (client) {
    await client.stop();
    client = undefined;
  }
}

function createServerOptions(
  context: vscode.ExtensionContext,
  configuredPath: string,
  configuredArgs: string[],
): ServerOptions {
  if (configuredPath.trim().length > 0) {
    return {
      command: configuredPath,
      args: configuredArgs,
      options: { cwd: vscode.workspace.workspaceFolders?.[0]?.uri.fsPath },
    };
  }

  const bundledDll = path.join(context.extensionPath, 'server', 'Square.LanguageServer.dll');
  return {
    command: 'dotnet',
    args: [bundledDll, ...configuredArgs],
    transport: TransportKind.stdio,
    options: { cwd: vscode.workspace.workspaceFolders?.[0]?.uri.fsPath },
  };
}
