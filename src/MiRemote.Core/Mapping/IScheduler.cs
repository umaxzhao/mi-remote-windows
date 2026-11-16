using System;
using System.Collections.Generic;

namespace MiRemote.Core
{
    /// <summary>
    /// 引擎的调度抽象。生产实现用实时线程/定时器；自测用虚拟时钟确定性推进（移植 macOS 版 ManualClock）。
    /// </summary>
    public interface IScheduler
    {
        void Post(Action work);
        void Schedule(int delayMs, Action work);
        /// <summary>投递到 UI 线程；测试里直接就地执行。</summary>
        void PostUi(Action work);
        long NowMs { get; }
    }

    /// <summary>生产调度器：Post 直接串行执行（引擎内部加锁），Schedule 用一次性定时器。</summary>
    public sealed class RealScheduler : IScheduler
    {
        private readonly Action<Action>? _uiDispatcher;

        public RealScheduler(Action<Action>? uiDispatcher = null) => _uiDispatcher = uiDispatcher;

        public long NowMs => Environment.TickCount64;

        public void Post(Action work) => work();

        public void Schedule(int delayMs, Action work)
        {
            if (delayMs <= 0) { work(); return; }
            // 一次性定时器：引擎内部用自增令牌作废过期回调，无需持有 Timer 句柄。
            new System.Threading.Timer(_ => work(), null, delayMs, System.Threading.Timeout.Infinite);
        }

        public void PostUi(Action work)
        {
            if (_uiDispatcher is not null) _uiDispatcher(work);
            else work();
        }
    }

    /// <summary>虚拟时钟（自测用）：登记延迟回调，advance 按时间序触发。</summary>
    public sealed class ManualClock : IScheduler
    {
        private long _now;
        private readonly List<(long At, Action Work)> _pending = new();

        public long NowMs => _now;

        public void Post(Action work) => work();

        public void Schedule(int delayMs, Action work) => _pending.Add((_now + Math.Max(0, delayMs), work));

        public void PostUi(Action work) => work();

        public void Advance(int ms)
        {
            long target = _now + ms;
            while (true)
            {
                int idx = -1;
                long best = long.MaxValue;
                for (int i = 0; i < _pending.Count; i++)
                    if (_pending[i].At <= target && _pending[i].At < best) { best = _pending[i].At; idx = i; }
                if (idx < 0) break;
                var item = _pending[idx];
                _pending.RemoveAt(idx);
                _now = item.At;
                item.Work();
            }
            _now = target;
        }
    }
}
