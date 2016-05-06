#pragma once
#include "ADT/ArrayRef.h"
#include "EventInfoCache.h"
#include "ITraceProcessor.h"
#include "ITraceSession.h"
#include "Support/Allocator.h"

#include <atomic>
#include <deque>
#include <windows.h>
#include <thread>
#include <evntcons.h>

namespace etk
{

class TraceFormatter
{
public:
    bool FormatEventMessage(
        EventInfo info, size_t pointerSize, std::wstring& sink);

private:
    std::wstring formattedProperties;
    std::vector<size_t> formattedPropertiesOffsets;
};

class EtwTraceProcessor : public ITraceProcessor
{
public:
    EtwTraceProcessor(std::wstring loggerName, ArrayRef<TraceProviderSpec> providers);
    virtual ~EtwTraceProcessor();

    virtual void SetEventSink(IEventSink* sink) override;
    virtual void StartProcessing() override;
    virtual void StopProcessing() override;
    virtual bool IsEndOfTracing() override;

    virtual void ClearEvents() override
    {
        eventCount = 0;
        events.clear();
        if (sink)
            sink->NotifyNewEvents(0);
    }

    virtual size_t GetEventCount() override { return eventCount; }
    virtual EVENT_RECORD const* GetEvent(size_t index) override
    {
        if (index >= eventCount) return nullptr;
        return events[index];
    }

private:
    static DWORD WINAPI ProcessTraceProc(_In_ LPVOID lpParameter);
    static VOID WINAPI EventRecordCallback(_In_ PEVENT_RECORD EventRecord);

    void OnProcessTrace();
    void OnEvent(EVENT_RECORD* eventRecord);

    void RegisterManifests();
    void UnregisterManifests();

    std::wstring loggerName;
    std::thread processorThread;
    TRACEHANDLE traceHandle;
    EVENT_TRACE_LOGFILEW traceLogFile;
    EventInfoCache eventInfoCache;
    std::vector<std::wstring> manifests;
    std::vector<std::wstring> providerBinaries;

    using EventRecordAllocator = BumpPtrAllocator<MallocAllocator>;
    EventRecordAllocator eventRecordAllocator;

    std::deque<EVENT_RECORD const*> events;
    std::atomic<size_t> eventCount;

    IEventSink* sink = nullptr;
    TraceFormatter formatter;
    std::wstring messageBuffer;
};

} // namespace etk