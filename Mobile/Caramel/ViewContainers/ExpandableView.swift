import SwiftUI

struct ExpandableView: View {
    
    @Namespace private var namespace
    @State private var show = false
    
    var thumbnail: ThumbnailView
    var expanded: ExpandedView
    
    var body: some View {
        ZStack {
            if !show {
                thumbnailView()
            } else {
                expandedView()
            }
        }
        .onTapGesture {
            if !show {
                withAnimation (.spring(response: 0.6, dampingFraction: 0.8)){
                    show.toggle()
                }
            }
        }
    }
    
    @ViewBuilder
    private func thumbnailView() -> some View {
        ZStack {
            thumbnail
                .matchedGeometryEffect(id: "view", in: namespace)
        }
        .mask(
            RoundedRectangle(cornerRadius: 50, style: .continuous)
                .matchedGeometryEffect(id: "mask", in: namespace)
        )
        
    }
    
    @ViewBuilder
    private func expandedView() -> some View {
        VStack{
            ZStack {
                expanded
                    .matchedGeometryEffect(id: "view", in: namespace)
                    .mask(
                        RoundedRectangle(cornerRadius: 20, style: .continuous)
                            .matchedGeometryEffect(id: "mask", in: namespace)
                    )
            }
            
            Button {
                withAnimation(.spring(response: 0.6, dampingFraction: 0.8)) {
                    show.toggle()
                }
            } label: {
                Image(systemName: "chevron.down").padding(25)
            }
            .foregroundStyle(.secondary)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
            .matchedGeometryEffect(id: "mask", in: namespace)
        }
    }
}
