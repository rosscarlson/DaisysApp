plugins {
    id("com.android.application") version "8.13.2" apply false
    id("org.jetbrains.kotlin.android") version "2.2.21" apply false
}

// Built outside the source folder: OneDrive (where this repo lives) locks files Gradle is replacing.
allprojects {
    layout.buildDirectory.set(File(System.getProperty("java.io.tmpdir"), "b2kr-build/${project.name}"))
}
