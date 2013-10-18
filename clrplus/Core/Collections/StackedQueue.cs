using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClrPlus.Core.Collections {
    using System.Collections;
    using System.Collections.Concurrent;
    using Extensions;

    public class StackedQueue<T> : IEnumerable<T>  {

        private ConcurrentStack<ConcurrentQueue<T>> _queues = new ConcurrentStack<ConcurrentQueue<T>>();
        private ConcurrentQueue<T> _defaultQueue;

        public class SQEnumerator<tt> : IEnumerator<tt> {
            private ConcurrentStack<ConcurrentQueue<tt>> _queues;
            private IEnumerator<ConcurrentQueue<tt>> _stackEnumerator;
            private IEnumerator<tt> _queueEnumerator;

            internal SQEnumerator(StackedQueue<tt> sq) {
                _queues = sq._queues;
                Reset();
            }

            public void Dispose() {
                DisposeStackEnumerator();
                DisposeQueueEnumerator();
                _queues = null;
            }

            private void DisposeStackEnumerator() {
                if (_stackEnumerator != null) {
                    _stackEnumerator.Dispose();
                }
                _stackEnumerator = null;
            }

            private void DisposeQueueEnumerator() {
                if (_queueEnumerator != null) {
                    _queueEnumerator.Dispose();
                }
                _queueEnumerator = null;
            }

            public bool MoveNext() {
                if (_stackEnumerator == null) {
                    return false;
                }

                if (_queueEnumerator == null) {
                    if (_stackEnumerator.MoveNext() == false) {
                        DisposeStackEnumerator();
                        return false;
                    }
                    _queueEnumerator = _stackEnumerator.Current.GetEnumerator();
                }

                if (_queueEnumerator.MoveNext() == false) {
                    DisposeQueueEnumerator();
                    return MoveNext();
                }

                return true;
            }

            public void Reset() {
                DisposeStackEnumerator();
                DisposeQueueEnumerator();
                _stackEnumerator = _queues.GetEnumerator();
            }

            public tt Current {
                get {
                    return _queueEnumerator.Current;
                }
            }

            object IEnumerator.Current {
                get {
                    return Current;
                }
            }
        }

        public StackedQueue() {
            _queues.Push(_defaultQueue = new ConcurrentQueue<T>());
        }

        public StackedQueue(IEnumerable<T> items): this() {
            Enqueue(items);
        }

        public IEnumerator<T> GetEnumerator() {
            return new SQEnumerator<T>(this);
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        public int Count {
            get {
                return _queues.Sum(each => each.Count);
            }
        }

        public void Enqueue(IEnumerable<T> items) {
            foreach (var i in items) {
                Enqueue(i);
            }
        }

        public void Enqueue(T item) {
            _defaultQueue.Enqueue(item);
        }

        public void EnqueueHighPriority(T item) {
            _queues.Push(new ConcurrentQueue<T>(item.SingleItemAsEnumerable()));
        }

        public void EnqueueHighPriority(IEnumerable<T> items) {
            _queues.Push( new ConcurrentQueue<T>(items));
        }

        public bool IsEmpty {
            get {
                return _queues.All(each => each.IsEmpty);
            }
        }

        public bool TryDequeue(out T item, out bool isHighPriority) {
            if (_queues.Count > 1) {
                ConcurrentQueue<T> pk;

                if (_queues.TryPeek(out pk)) {
                    if (pk.Count == 0 || !pk.TryDequeue(out item)) {
                        _queues.TryPop(out pk);
                        return TryDequeue(out item, out isHighPriority);
                    }
                    isHighPriority = true;
                    return true;
                }
                item = default(T);
                isHighPriority = false;
                return false;
            }
            isHighPriority = false;
            return _defaultQueue.TryDequeue(out item);
        }
    }
}
