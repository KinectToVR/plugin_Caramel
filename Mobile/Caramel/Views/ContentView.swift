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
                        resetInactivityTimer()
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
            resetInactivityTimer()
        }
        .onTapGesture(count: 2) {
            withAnimation(.easeInOut(duration: 0.5)) {
                showOverlay.toggle()
            }
        }
        .onAppear {
            startInactivityTimer()
        }
        .onDisappear {
            timer?.invalidate()
        }
    }

    private func startInactivityTimer() {
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 30, repeats: false) {
            _ in
            withAnimation(.easeInOut(duration: 0.5)) {
                showOverlay = true
            }
        }
    }

    private func resetInactivityTimer() {
        lastTapTime = Date()
        startInactivityTimer()
    }
}

#Preview {
    ContentView()
}
