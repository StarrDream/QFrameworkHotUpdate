using System;
using System.Collections.Generic;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 简易最大堆优先队列（下载任务用）
    /// </summary>
    internal class PriorityQueue<T>
    {
        private readonly List<(int key, T value)> _heap = new List<(int, T)>();

        public int Count => _heap.Count;

        public void Enqueue(int key, T value)
        {
            _heap.Add((key, value));
            Up(_heap.Count - 1);
        }

        public T Dequeue()
        {
            if (_heap.Count == 0) throw new InvalidOperationException("Empty PQ");
            var root = _heap[0].value;
            var last = _heap[_heap.Count - 1];
            _heap.RemoveAt(_heap.Count - 1);
            if (_heap.Count > 0)
            {
                _heap[0] = last;
                Down(0);
            }
            return root;
        }

        public void Clear() => _heap.Clear();

        void Up(int i)
        {
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (_heap[i].key <= _heap[p].key) break;
                (_heap[i], _heap[p]) = (_heap[p], _heap[i]);
                i = p;
            }
        }

        void Down(int i)
        {
            int n = _heap.Count;
            while (true)
            {
                int l = i * 2 + 1;
                int r = l + 1;
                int largest = i;
                if (l < n && _heap[l].key > _heap[largest].key) largest = l;
                if (r < n && _heap[r].key > _heap[largest].key) largest = r;
                if (largest == i) break;
                (_heap[i], _heap[largest]) = (_heap[largest], _heap[i]);
                i = largest;
            }
        }
    }
}