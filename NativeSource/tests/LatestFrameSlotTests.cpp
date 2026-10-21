#include "svp/LatestFrameSlot.hpp"
#include <atomic>
#include <cstdlib>
#include <iostream>
#include <thread>

static void require(bool condition, const char* message) {
    if (!condition) { std::cerr << message << '\n'; std::exit(1); }
}

int main() {
    svp::LatestFrameSlot<int> slot;
    require(!slot.publish(nullptr), "null frame accepted");
    auto first = std::make_shared<int>(1);
    std::weak_ptr<int> retired = first;
    slot.publish(std::move(first));
    slot.publish(std::make_shared<int>(2));
    require(retired.expired(), "replaced frame retained");
    require(slot.replaced() == 1, "incorrect replacement count");
    auto newest = slot.take();
    require(newest && *newest == 2 && !slot.take(), "consumer did not receive newest frame exactly once");

    // A deleter re-entering the slot must not deadlock while replacing/closing.
    bool reentered = false;
    slot.publish(std::shared_ptr<int>(new int(3), [&](int* ptr) {
        (void)slot.replaced(); reentered = true; delete ptr;
    }));
    slot.publish(std::make_shared<int>(4));
    require(reentered, "retired frame deleter was not invoked");
    slot.close();
    require(!slot.take() && !slot.publish(std::make_shared<int>(5)), "closed slot accepted work");

    svp::LatestFrameSlot<int> concurrent;
    std::atomic<bool> done{false};
    std::thread producer([&] {
        for (int i = 1; i <= 100000; ++i) concurrent.publish(std::make_shared<int>(i));
        done.store(true, std::memory_order_release);
    });
    int previous = 0;
    while (!done.load(std::memory_order_acquire)) {
        if (auto frame = concurrent.take()) {
            require(*frame > previous, "consumer saw duplicate/out-of-order frames");
            previous = *frame;
        }
        std::this_thread::yield();
    }
    producer.join();
    if (auto frame = concurrent.take()) previous = *frame;
    require(previous == 100000, "last frame was lost");
    concurrent.close();

    // Race shutdown with publication; ownership must always be released.
    svp::LatestFrameSlot<int> closing;
    std::atomic<int> alive{0};
    std::thread racingProducer([&] {
        for (int i = 0; i < 10000; ++i) {
            ++alive;
            closing.publish(std::shared_ptr<int>(new int(i), [&](int* ptr) { --alive; delete ptr; }));
        }
    });
    closing.close();
    racingProducer.join();
    require(alive == 0, "shutdown leaked a frame");
    std::cout << "LatestFrameSlot: replacement, ownership, reentrancy, ordering, shutdown passed\n";
}
