import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// The release key isn't in the repo: keystore.properties (storeFile, storePassword, keyAlias, keyPassword) is found
// through the B2KR_KEYSTORE property or environment variable (see README.md). Without it, release builds aren't signed.
val keystoreFile = (findProperty("B2KR_KEYSTORE") as String?) ?: System.getenv("B2KR_KEYSTORE")
val keystore = keystoreFile?.let { f -> Properties().apply { file(f).inputStream().use { load(it) } } }

android {
    namespace = "com.jasbone.butt2keyremote"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.jasbone.butt2keyremote"
        minSdk = 23
        // (35, not 36: Android 16 ignores an app's orientation on tablets when it targets 36, and a ButtScreen
        // made tall or wide should turn the screen to fit)
        targetSdk = 35
        versionCode = 4
        versionName = "1.1.2"
    }

    signingConfigs {
        if (keystore != null) create("release") {
            storeFile = file(keystore.getProperty("storeFile"))
            storePassword = keystore.getProperty("storePassword")
            keyAlias = keystore.getProperty("keyAlias")
            keyPassword = keystore.getProperty("keyPassword")
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"))
            if (keystore != null) signingConfig = signingConfigs.getByName("release")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

kotlin {
    jvmToolchain(17)
}
