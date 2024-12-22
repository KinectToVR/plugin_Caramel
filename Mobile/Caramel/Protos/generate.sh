brew install swift-protobuf grpc-swift

protoc --swift_out=. service.proto
protoc --plugin=/opt/homebrew/bin/protoc-gen-swift --grpc-swift_out=. service.proto

