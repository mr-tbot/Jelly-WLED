// AGP 9 applies the Kotlin plugin itself ("built-in Kotlin"), so org.jetbrains.kotlin.android must not be
// applied anywhere. The Kotlin version is pinned on the buildscript classpath, the documented way to run a
// newer Kotlin Gradle plugin than the one AGP bundles. Same arrangement as TBOT's Lang-BOT and Sid3Kick apps.
buildscript {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
    dependencies {
        classpath("org.jetbrains.kotlin:kotlin-gradle-plugin:2.4.10")
    }
}

plugins {
    id("com.android.application") version "9.3.1" apply false
    id("org.jetbrains.kotlin.plugin.compose") version "2.4.10" apply false
}
