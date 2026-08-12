package com.wuldas.square;

import com.intellij.openapi.application.PathManager;
import org.jetbrains.plugins.textmate.api.TextMateBundleProvider;

import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.List;

public final class SquareTextMateBundleProvider implements TextMateBundleProvider {
    private static final String RESOURCE_ROOT = "/textmate/Square.tmBundle/";
    private static final List<String> RESOURCE_FILES = List.of(
        "info.plist",
        "Syntaxes/sqx.tmLanguage.json",
        "Syntaxes/sqv.tmLanguage.json"
    );

    @Override
    public List<PluginBundle> getBundles() {
        Path bundle = PathManager.getSystemDir()
            .resolve("square-language-support")
            .resolve("Square.tmBundle");
        try {
            for (String relativePath : RESOURCE_FILES) {
                copyResource(relativePath, bundle.resolve(relativePath));
            }
        } catch (IOException exception) {
            throw new IllegalStateException("Unable to prepare the Square TextMate bundle", exception);
        }
        return List.of(new PluginBundle("Square", bundle));
    }

    private static void copyResource(String relativePath, Path destination) throws IOException {
        Files.createDirectories(destination.getParent());
        try (InputStream stream = SquareTextMateBundleProvider.class.getResourceAsStream(RESOURCE_ROOT + relativePath)) {
            if (stream == null) {
                throw new IOException("Missing bundled resource: " + relativePath);
            }
            Files.copy(stream, destination, StandardCopyOption.REPLACE_EXISTING);
        }
    }
}
