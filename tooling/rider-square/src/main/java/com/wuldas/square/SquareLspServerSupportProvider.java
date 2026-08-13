package com.wuldas.square;

import com.intellij.execution.configurations.GeneralCommandLine;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.vfs.VirtualFile;
import com.intellij.platform.lsp.api.LspServerSupportProvider;
import com.intellij.platform.lsp.api.ProjectWideLspServerDescriptor;

import java.io.IOException;
import java.io.InputStream;
import java.net.JarURLConnection;
import java.net.URISyntaxException;
import java.nio.file.Path;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;

public final class SquareLspServerSupportProvider implements LspServerSupportProvider {
    @Override
    public void fileOpened(
            Project project,
            VirtualFile file,
            LspServerStarter serverStarter) {
        if (isSquareFile(file)) {
            serverStarter.ensureServerStarted(new SquareLspServerDescriptor(project));
        }
    }

    private static boolean isSquareFile(VirtualFile file) {
        var extension = file.getExtension();
        return "sqx".equalsIgnoreCase(extension) || "sqv".equalsIgnoreCase(extension);
    }

    private static final class SquareLspServerDescriptor extends ProjectWideLspServerDescriptor {
        private SquareLspServerDescriptor(Project project) {
            super(project, "Square Language Server");
        }

        @Override
        public boolean isSupportedFile(VirtualFile file) {
            return isSquareFile(file);
        }

        @Override
        public GeneralCommandLine createCommandLine() {
            return new GeneralCommandLine("dotnet", bundledServerPath().toString());
        }

        private static Path bundledServerPath() {
            var resource = SquareLspServerSupportProvider.class
                    .getResource("/server/Square.LanguageServer.dll");
            if (resource == null) {
                throw new IllegalStateException(
                        "Bundled Square Language Server resource was not found");
            }

            try {
                if ("file".equalsIgnoreCase(resource.getProtocol())) {
                    return Path.of(resource.toURI());
                }
                if ("jar".equalsIgnoreCase(resource.getProtocol())) {
                    return extractBundledServer((JarURLConnection) resource.openConnection());
                }
                throw new IllegalStateException(
                        "Unsupported bundled Square Language Server URL: " + resource);
            } catch (IOException | URISyntaxException exception) {
                throw new IllegalStateException(
                        "Cannot locate bundled Square Language Server", exception);
            }
        }

        private static Path extractBundledServer(JarURLConnection connection) throws IOException {
            connection.setUseCaches(false);
            var jarFileUrl = connection.getJarFileURL().toExternalForm();
            var cacheKey = Integer.toHexString(jarFileUrl.hashCode());
            var serverDirectory = Path.of(
                    System.getProperty("java.io.tmpdir"),
                    "square-language-server",
                    cacheKey,
                    "server");
            var serverAssembly = serverDirectory.resolve("Square.LanguageServer.dll");
            if (Files.isRegularFile(serverAssembly)) {
                return serverAssembly;
            }

            Files.createDirectories(serverDirectory);
            try (JarFile jar = connection.getJarFile()) {
                var entries = jar.entries();
                while (entries.hasMoreElements()) {
                    JarEntry entry = entries.nextElement();
                    if (entry.isDirectory() || !entry.getName().startsWith("server/")) {
                        continue;
                    }

                    var relativePath = entry.getName().substring("server/".length());
                    var target = serverDirectory.resolve(relativePath).normalize();
                    if (!target.startsWith(serverDirectory)) {
                        throw new IOException("Invalid bundled server entry: " + entry.getName());
                    }
                    Files.createDirectories(target.getParent());
                    try (InputStream input = jar.getInputStream(entry)) {
                        Files.copy(input, target, StandardCopyOption.REPLACE_EXISTING);
                    }
                }
            }

            if (!Files.isRegularFile(serverAssembly)) {
                throw new IOException("Bundled Square Language Server assembly was not extracted");
            }
            return serverAssembly;
        }
    }
}
