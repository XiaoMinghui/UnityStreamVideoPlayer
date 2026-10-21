#pragma once
#include <cstdint>
#include <memory>
#include <mutex>
#include <utility>

namespace svp {
// One pending decoded frame. A slow renderer never grows a FIFO of old frames.
// This is NOT a queue for compressed packets (which have reference dependencies).
// Frame destruction is deliberately outside the mutex; deleters may call back into a pool.
template<class T>
class LatestFrameSlot final {
public:
    bool publish(std::shared_ptr<T> frame) {
        if (!frame) return false;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (closed_) return false;
            if (frame_) ++replaced_;
            frame_.swap(frame);
        }
        return true;
    }

    std::shared_ptr<T> take() {
        std::lock_guard<std::mutex> lock(mutex_);
        return std::exchange(frame_, {});
    }

    void close() {
        std::shared_ptr<T> retired;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            closed_ = true;
            retired.swap(frame_);
        }
    }

    uint64_t replaced() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return replaced_;
    }

private:
    mutable std::mutex mutex_;
    std::shared_ptr<T> frame_;
    uint64_t replaced_ = 0;
    bool closed_ = false;
};
}
