import SwiftUI

struct ContentView: View {
    @State private var showOverlay = false
    @State private var lastTapTime = Date()
    @State private var timer: Timer?

    var body: some View {
        let settingsView = SettingsView()

        ZStack {
            ARViewContainer(settings: settingsView)
                .edgesIgnoringSafeArea(.all)
                .overlay(settingsView, alignment: .bottom)

            if showOverlay {
                Color.black
                    .edgesIgnoringSafeArea(.all)
                    .onTapGesture {
                        resetInactivityTimer(settings: settingsView)
                        withAnimation(.easeInOut(duration: 0.5)) {
                            showOverlay.toggle()
                        }
                    }
                    .transition(.opacity)
            }
        }
        .statusBarHidden(showOverlay)
        .persistentSystemOverlays(showOverlay ? (.hidden) : (.automatic))
        .toolbar(.hidden, for: .tabBar)
        .onTapGesture {
            resetInactivityTimer(settings: settingsView)
        }
        .onTapGesture(count: 2) {
            withAnimation(.easeInOut(duration: 0.5)) {
                showOverlay.toggle()
            }
        }
        .onAppear {
            startInactivityTimer(settings: settingsView)
        }
        .onDisappear {
            timer?.invalidate()
        }
    }

    private func startInactivityTimer(settings: SettingsView) {
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 30, repeats: false) {
            _ in
            if settings.blockInteractions {
                startInactivityTimer(settings: settings)
            } else {
                withAnimation(.easeInOut(duration: 0.5)) {
                    showOverlay = true
                }
            }
        }
    }

    private func resetInactivityTimer(settings: SettingsView) {
        lastTapTime = Date()
        startInactivityTimer(settings: settings)
    }
}

#Preview {
    ContentView()
}
