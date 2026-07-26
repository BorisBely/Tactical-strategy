using System;
using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public sealed class VehicleOrderQueue
	{
		private readonly Queue<VehicleMoveOrder> m_Queue = new Queue<VehicleMoveOrder>();
		private VehicleMoveOrder m_Current;
		private VehicleMoveOrder m_PendingInterrupt;

		private readonly float m_MinOrderSeparation;
		private readonly float m_OrderMergeDistance;

		public int Count => m_Queue.Count;
		public bool HasCurrent => m_Current != null && m_Current.State == OrderState.Executing;
		public bool HasPendingInterrupt => m_PendingInterrupt != null;
		public VehicleMoveOrder CurrentOrder => m_Current;

		public IReadOnlyList<VehicleMoveOrder> QueuedOrders
		{
			get
			{
				var list = new List<VehicleMoveOrder>(m_Queue.Count);
				foreach (var o in m_Queue)
					list.Add(o);
				return list;
			}
		}

		public VehicleOrderQueue(float _minOrderSeparation = 1.5f, float _orderMergeDistance = 2.0f)
		{
			m_MinOrderSeparation = _minOrderSeparation;
			m_OrderMergeDistance = _orderMergeDistance;
		}

		/// <summary>Добавить приказ в конец очереди. EmergencyStop прерывает всё.</summary>
		public void Enqueue(VehicleMoveOrder _order)
		{
			if (_order == null)
				return;

			if (_order.Type == VehicleOrderType.EmergencyStop)
			{
				CancelAll("emergency-stop");
				m_Current = null;
				m_PendingInterrupt = _order;
				_order.MarkInterrupting();
				return;
			}

			if (_order.Type == VehicleOrderType.Stop)
			{
				m_PendingInterrupt = _order;
				_order.MarkInterrupting();
				return;
			}

			if (_order.Type == VehicleOrderType.Move && _order.HasDestination)
			{
				VehicleMoveOrder last = PeekLast();
				if (last != null && last.HasDestination &&
				    Vector3.Distance(last.Destination, _order.Destination) < m_OrderMergeDistance)
				{
					ReplaceLast(_order);
					return;
				}
			}

			m_Queue.Enqueue(_order);
		}

		/// <summary>Вставить приказ в начало очереди.</summary>
		public void EnqueueFront(VehicleMoveOrder _order)
		{
			if (_order == null)
				return;

			var list = new List<VehicleMoveOrder>(m_Queue.Count + 1);
			list.Add(_order);
			while (m_Queue.Count > 0)
				list.Add(m_Queue.Dequeue());

			m_Queue.Clear();
			for (int i = 0; i < list.Count; i++)
				m_Queue.Enqueue(list[i]);
		}

		/// <summary>Очистить всю очередь и текущий приказ.</summary>
		public void Clear()
		{
			while (m_Queue.Count > 0)
			{
				var o = m_Queue.Dequeue();
				o.MarkAborted();
			}

			if (m_Current != null)
			{
				m_Current.MarkAborted();
				m_Current = null;
			}

			m_PendingInterrupt = null;
		}

		/// <summary>Emergency: очистить всё, текущий и очередь. Безвозвратно.</summary>
		public void CancelAll(string _reason)
		{
			while (m_Queue.Count > 0)
			{
				var o = m_Queue.Dequeue();
				o.MarkAborted();
			}

			if (m_Current != null)
			{
				m_Current.MarkAborted();
				m_Current = null;
			}

			m_PendingInterrupt = null;
		}

		/// <summary>Мягкая остановка: прервать текущий приказ, но сохранить очередь.</summary>
		public void CancelCurrent(string _reason)
		{
			if (m_Current != null)
			{
				m_Current.MarkAborted();
				m_Current = null;
			}
		}

		/// <summary>Посмотреть следующий приказ без извлечения.</summary>
		public bool TryPeek(out VehicleMoveOrder _order)
		{
			if (m_Queue.Count > 0)
			{
				_order = m_Queue.Peek();
				return true;
			}

			_order = null;
			return false;
		}

		/// <summary>Извлечь следующий приказ из очереди.</summary>
		public bool TryDequeue(out VehicleMoveOrder _order)
		{
			if (m_Queue.Count > 0)
			{
				_order = m_Queue.Dequeue();
				return true;
			}

			_order = null;
			return false;
		}

		/// <summary>Продвинуть следующий приказ из очереди в текущий.</summary>
		public VehicleMoveOrder PromoteNext(float _timeNow)
		{
			if (m_Current != null)
			{
				m_Current.MarkCompleted();
				m_Current = null;
			}

			if (m_PendingInterrupt != null)
			{
				m_Current = m_PendingInterrupt;
				m_PendingInterrupt = null;
				m_Current.MarkStarted(_timeNow);
				return m_Current;
			}

			if (TryDequeue(out VehicleMoveOrder next))
			{
				m_Current = next;
				m_Current.MarkStarted(_timeNow);
				return m_Current;
			}

			return null;
		}

		/// <summary>Продвинуть pending-прерывание в текущий приказ (Stop/EmergencyStop).</summary>
		public bool TryPromoteInterrupt(float _timeNow)
		{
			if (m_PendingInterrupt == null)
				return false;

			if (m_Current != null)
			{
				m_Current.MarkAborted();
				m_Current = null;
			}

			m_Current = m_PendingInterrupt;
			m_PendingInterrupt = null;
			m_Current.MarkStarted(_timeNow);
			return true;
		}

		/// <summary>Отметить, что FSM начал выполнение текущего приказа.</summary>
		public void MarkCurrentOrderStarted(float _timeNow)
		{
			if (m_Current != null)
				m_Current.MarkStarted(_timeNow);
		}

		/// <summary>Отметить, что текущий приказ успешно завершён.</summary>
		public void MarkCurrentOrderCompleted()
		{
			if (m_Current != null)
				m_Current.MarkCompleted();
		}

		/// <summary>Отметить, что текущий приказ прерван снаружи.</summary>
		public void MarkCurrentOrderAborted()
		{
			if (m_Current != null)
				m_Current.MarkAborted();
		}

		/// <summary>Удалить просроченные приказы из очереди.</summary>
		public void RemoveExpiredOrders(float _timeNow)
		{
			if (m_Current != null &&
			    m_Current.TimeoutSeconds > 0f &&
			    m_Current.State == OrderState.Executing &&
			    (_timeNow - m_Current.CreatedTime) > m_Current.TimeoutSeconds)
			{
				m_Current.MarkExpired();
				m_Current = null;
			}

			var kept = new Queue<VehicleMoveOrder>();
			while (m_Queue.Count > 0)
			{
				var order = m_Queue.Dequeue();
				if (order.TimeoutSeconds > 0f &&
				    (_timeNow - order.CreatedTime) > order.TimeoutSeconds)
				{
					order.MarkExpired();
					continue;
				}
				kept.Enqueue(order);
			}

			while (kept.Count > 0)
				m_Queue.Enqueue(kept.Dequeue());
		}

		private VehicleMoveOrder PeekLast()
		{
			VehicleMoveOrder last = null;
			foreach (var order in m_Queue)
				last = order;
			return last;
		}

		private void ReplaceLast(VehicleMoveOrder _order)
		{
			var list = new List<VehicleMoveOrder>(m_Queue.Count);
			while (m_Queue.Count > 1)
				list.Add(m_Queue.Dequeue());

			if (m_Queue.Count > 0)
				m_Queue.Dequeue().MarkAborted();

			m_Queue.Clear();
			for (int i = 0; i < list.Count; i++)
				m_Queue.Enqueue(list[i]);
			m_Queue.Enqueue(_order);
		}
	}
}
