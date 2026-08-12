plugins {
    java
    id("org.jetbrains.intellij.platform") version "2.18.1"
}

group = "com.wuldas.square"
version = "0.1.0"

val localRiderPath = providers.gradleProperty("localRiderPath")

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        if (localRiderPath.isPresent) {
            local(localRiderPath.get())
        } else {
            rider("2025.2.3") {
                useInstaller = false
            }
        }
        bundledPlugin("org.jetbrains.plugins.textmate")
    }
}

java {
    toolchain {
        languageVersion.set(JavaLanguageVersion.of(25))
    }
}

tasks.withType<JavaCompile>().configureEach {
    options.release.set(21)
}

val generatedResources = layout.buildDirectory.dir("generated-resources")
val syncTextMateBundle by tasks.registering(Copy::class) {
    from("../square-language/syntaxes") {
        include("sqx.tmLanguage.json", "sqv.tmLanguage.json")
        into("textmate/Square.tmBundle/Syntaxes")
    }
    from("src/main/textmate") {
        into("textmate/Square.tmBundle")
    }
    from("../vscode-square/LICENSE.txt")
    into(generatedResources)
}

sourceSets.main {
    resources.srcDir(generatedResources)
}

tasks.processResources {
    dependsOn(syncTextMateBundle)
}

intellijPlatform {
    pluginConfiguration {
        ideaVersion {
            sinceBuild = "252"
        }
    }
    pluginVerification {
        ides {
            if (localRiderPath.isPresent) {
                local(localRiderPath)
            } else {
                current()
            }
        }
    }
}

tasks.buildSearchableOptions {
    enabled = false
}
