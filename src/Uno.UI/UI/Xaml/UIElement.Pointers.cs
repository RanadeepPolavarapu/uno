﻿using System;
using System.Diagnostics;
using System.Linq;
using Windows.UI.Input;
using Windows.UI.Xaml.Input;
using Uno.Extensions;
using Uno.Logging;

namespace Windows.UI.Xaml
{
	/*
	 *	This partial file
	 *		- Ensures to raise the right PointerXXX events sequences
	 *		- Handles the gestures events registration, and adjusts the configuration of the GestureRecognizer accordingly
	 *		- Forwards the pointers events to the gesture recognizer and then raise back the recognized gesture events
	 *	
	 *	The API exposed by this file to its native parts are:
	 *		partial void InitializePointersPartial();
	 *		partial void OnManipulationModeChanged(ManipulationModes mode);
	 *		private bool RaiseNativelyBubbledDown(PointerRoutedEventArgs args);
	 *		private bool RaiseNativelyBubbledMove(PointerRoutedEventArgs args);
	 *		private bool RaiseNativelyBubbledUp(PointerRoutedEventArgs args);
	 *		private bool RaiseNativelyBubbledLost(PointerRoutedEventArgs args);
	 *
	 * 	The native components are responsible to subscribe to the native touches events,
	 *	create the corresponding PointerEventArgs and then invoke one of the "RaiseNativelyBubbledXXX" method.
	 *
	 *	This file implements the following from the "RoutedEvents"
	 *		partial void AddHandlerPartial(RoutedEvent routedEvent, int handlersCount, object handler, bool handledEventsToo);
	 * 		partial void RemoveHandlerPartial(RoutedEvent routedEvent, int remainingHandlersCount, object handler);
	 *	and is using:
	 *		internal bool RaiseEvent(RoutedEvent routedEvent, RoutedEventArgs args);
	 */

	partial class UIElement
	{
		#region ManipulationMode (DP)
		public static DependencyProperty ManipulationModeProperty { get; } = DependencyProperty.Register(
			"ManipulationMode",
			typeof(ManipulationModes),
			typeof(UIElement),
			new FrameworkPropertyMetadata(ManipulationModes.System, FrameworkPropertyMetadataOptions.None, OnManipulationModeChanged));

		private static void OnManipulationModeChanged(DependencyObject snd, DependencyPropertyChangedEventArgs args)
		{
			if (snd is UIElement elt)
			{
				elt.OnManipulationModeChanged((ManipulationModes)args.NewValue);
			}
		}

		partial void OnManipulationModeChanged(ManipulationModes mode);

		public ManipulationModes ManipulationMode
		{
			get => (ManipulationModes)this.GetValue(ManipulationModeProperty);
			set => this.SetValue(ManipulationModeProperty, value);
		}
		#endregion

		#region IsPointerPressed (Internal property with overridable callback)
		private bool _isPointerPressed;

		/// <summary>
		/// Indicates if a pointer was pressed while over the element (i.e. PressedState)
		/// </summary>
		internal bool IsPointerPressed
		{
			get => _isPointerPressed;
			set // TODO: This should be private, but we need to update all controls that are setting
			{
				if (_isPointerPressed != value)
				{
					_isPointerPressed = value;
					OnIsPointerPressedChanged(value);
				}
			}
		}

		internal virtual void OnIsPointerPressedChanged(bool isPointerPressed)
		{
		}
		#endregion

		#region IsPointerOver (Internal property with overridable callback)
		private bool _isPointerOver;

		/// <summary>
		/// Indicates if a pointer (no matter the pointer) is currently over the element (i.e. OverState)
		/// </summary>
		internal bool IsPointerOver
		{
			get => _isPointerOver;
			set // TODO: This should be private, but we need to update all controls that are setting
			{
				if (_isPointerOver != value)
				{
					_isPointerOver = value;
					OnIsPointerOverChanged(value);
				}
			}
		}

		internal virtual void OnIsPointerOverChanged(bool isPointerOver)
		{
		}
		#endregion

#if __IOS__ || __WASM__ // This is temporary until all platforms Pointers have been reworked

		private /* readonly but partial */ Lazy<GestureRecognizer> _gestures;

		// ctor
		private void InitializePointers()
		{
			_gestures = new Lazy<GestureRecognizer>(CreateGestureRecognizer);
			InitializePointersPartial();
		}

		partial void InitializePointersPartial();

		private GestureRecognizer CreateGestureRecognizer()
		{
			var recognizer = new GestureRecognizer();

			recognizer.Tapped += OnTapRecognized;

			// Allow partial parts to subscribe to pointer events (WASM)
			OnGestureRecognizerInitialized(recognizer);

			return recognizer;

			void OnTapRecognized(GestureRecognizer sender, TappedEventArgs args)
			{
				if (args.TapCount == 1)
				{
					RaiseEvent(TappedEvent, new TappedRoutedEventArgs(args.PointerDeviceType, args.Position));
				}
				else // i.e. args.TapCount == 2
				{
					RaiseEvent(DoubleTappedEvent, new DoubleTappedRoutedEventArgs(args.PointerDeviceType, args.Position));
				}
			}
		}

		partial void OnGestureRecognizerInitialized(GestureRecognizer recognizer);

		#region Add/Remove handler
		partial void AddGestureHandler(RoutedEvent routedEvent, int handlersCount, object handler, bool handledEventsToo)
		{
			if (handlersCount == 1)
			{
				// If greater than 1, it means that we already enabled the setting (and if lower than 0 ... it's weird !)
				ToggleGesture(routedEvent);
			}
		}

		partial void RemoveGestureHandler(RoutedEvent routedEvent, int remainingHandlersCount, object handler)
		{
			if (remainingHandlersCount == 0)
			{
				ToggleGesture(routedEvent);
			}
		}

		private void ToggleGesture(RoutedEvent routedEvent)
		{
			if (routedEvent == TappedEvent)
			{
				_gestures.Value.GestureSettings |= GestureSettings.Tap;
			}
			else if (routedEvent == DoubleTappedEvent)
			{
				_gestures.Value.GestureSettings |= GestureSettings.DoubleTap;
			}
		}
		#endregion

#if __IOS__ || __WASM__
		#region Raise pointer events and gesture recognition

		private bool OnNativePointerEnter(PointerRoutedEventArgs args)
		{
			return SetOver(args, true);
		}

		private bool OnNativePointerDown(PointerRoutedEventArgs args)
		{
			//IsPointerPressed = true;

			//// 3. Raise the pressed event
			//args.Handled = false;
			//var handledInManaged = RaiseEvent(PointerPressedEvent, args);

			var handledInManaged = SetPressed(args, true);

			// 4. Process gestures
			// Note: We process the DownEvent *after* the Raise(Pressed), so in case of DoubleTapped
			//		 the event is fired after
			if (_gestures.IsValueCreated)
			{
				// We need to process only events that are bubbling natively to this control,
				// if they are bubbling in managed it means that they were handled by a child control,
				// so we should not use them for gesture recognition.
				_gestures.Value.ProcessDownEvent(args.GetCurrentPoint(this));
			}

			return handledInManaged;
		}

		private bool OnNativePointerMove(PointerRoutedEventArgs args)
		{
			var handledInManaged = false;
			var isOver = IsOver(args.Pointer);
			var isCaptured = IsCaptured(args.Pointer);

			// We are receiving an unexpected move for this pointer on this element,
			// we mute it to avoid invalid event sequence.
			// Notes:
			//   iOS:  This may happen on iOS where the pointers are implicitly captured.
			//   WASM: On wasm, if this check mutes your event, it's because you didn't received the "pointerenter" (not bubbling natively).
			//         This is usually because your control is covered by an element which is IsHitTestVisible == true.
			var isIrrelevant = !isOver && !isCaptured;
			if (isIrrelevant)
			{
				Debug.WriteLine("IGNORE MOVE");

				return handledInManaged; // Always false
			}

			args.Handled = false;
			handledInManaged = RaiseEvent(PointerMovedEvent, args);

			// 4. Process gestures
			if (_gestures.IsValueCreated)
			{
				// We need to process only events that are bubbling natively to this control,
				// if they are bubbling in managed it means that they were handled by a child control,
				// so we should not use them for gesture recognition.
				_gestures.Value.ProcessMoveEvents(args.GetIntermediatePoints(this));
			}

			return handledInManaged;
		}

		private bool OnNativePointerUp(PointerRoutedEventArgs args)
		{
			var handledInManaged = false;
			var isOver = IsOver(args.Pointer);
			var isCaptured = IsCaptured(args.Pointer);

			// we are receiving an unexpected up for this pointer on this control, handle it a cancel event in order to properly
			// update the state without raising invalid events (this is the case on iOS which implicitly captures pointers).
			var isIrrelevant = !isOver && !isCaptured; 

			handledInManaged |= SetPressed(args, false, isPointerCancelled: isIrrelevant);

			if (isIrrelevant)
			{
				return handledInManaged; // always false as SetPressed with isPointerCancelled==true always returns false;
			}

			// Note: We process the UpEvent between Release and Exited as the gestures like "Tap"
			//		 are fired between those events.
			if (_gestures.IsValueCreated)
			{
				// We need to process only events that are bubbling natively to this control,
				// if they are bubbling in managed it means that they where handled a child control,
				// so we should not use them for gesture recognition.
				_gestures.Value.ProcessUpEvent(args.GetCurrentPoint(this));
			}

			// We release the captures on up but only when pointer is not over the control (i.e. mouse that moved away)
			if (!isOver) // so isCaptured == true
			{
				handledInManaged |= ReleaseCaptures(args);
			}

			return handledInManaged;
		}

		private bool OnNativePointerExited(PointerRoutedEventArgs args)
		{
			var handledInManaged = false;

			handledInManaged |= SetOver(args, false);

			// We release the captures on exit when pointer is not pressed the control
			// Note: for a "Tap" with a finger the sequence is Up / Exited / Lost, so the lost cannot be raised on Up
			if (!IsPressed(args.Pointer))
			{
				handledInManaged |= ReleaseCaptures(args);
			}

			return handledInManaged;
		}

		private bool OnNativePointerCancel(PointerRoutedEventArgs args, bool isSwallowedBySystem)
		{
			var handledInManaged = false;
			var isOver = IsOver(args.Pointer);
			var isCaptured = IsCaptured(args.Pointer);

			// we are receiving an unexpected up for this pointer on this control, handle it a cancel event in order to properly
			// update the state without raising invalid events (this is the case on iOS which implicitly captures pointers).
			var isIrrelevant = !isOver && !isCaptured;

			// When a pointer is cancelled / swallowed by the system, we don't even receive "Released" nor "Exited"
			SetPressed(args, false, isPointerCancelled: true);
			SetOver(args, false, isPointerCancelled: true);

			if (isIrrelevant)
			{
				return handledInManaged; // always false
			}
		
			if (_gestures.IsValueCreated)
			{
				_gestures.Value.CompleteGesture();
			}

			if (isSwallowedBySystem)
			{
				handledInManaged |= ReleaseCaptures(args, forceCaptureLostEvent: true);
			}
			else
			{
				args.Handled = false;
				handledInManaged |= RaiseEvent(PointerCanceledEvent, args);
				handledInManaged |= ReleaseCaptures(args);
			}

			// 6. Release remaining pointer captures
			// If the pointer was natively captured, it means that we lost all managed captures
			// Note: We should have raise either PointerCaptureLost in 3. or PointerCancelled here in 6. depending of the reason which
			//		 drives the system to bubble a lost. However we don't have this kind of information on iOS, and it's
			//		 usually due to the ScrollView which kicks in. So we always raise the CaptureLost which is the behavior
			//		 on UWP when scroll starts (even if no capture are actives at this time).
			// ReleasePointerCaptures(); // Note this should raise the CaptureLost only if pointer was effectively captured TODO
			//args.Handled = false;
			//handledInManaged = ReleaseCaptures(args, isPointerCancelled: isSwallowedBySystem);

			return handledInManaged;
		}


		///// <summary>
		///// This should be invoked by the native part of the UIElement when a native touch starts
		///// </summary>
		//private bool RaiseNativelyBubbledDown(PointerRoutedEventArgs args)
		//{
		//	//var handledInManaged = false;

		//	//// 1. Update the state
		//	////var wasOver = IsPointerOver;
		//	////IsPointerOver = isOver;
		//	////IsPointerPressed = true; // we do not support multiple pointers at once

		//	//// 2. Raise enter if needed
		//	//// Note: Enter is raised *before* the Pressed
		//	////handledInManaged = SetOver(args, true); //RaiseEnteredOrExited(args, wasOver, isOver);

		//	//IsPointerPressed = true;

		//	//// 3. Raise the pressed event
		//	//args.Handled = false;
		//	//handledInManaged |= RaiseEvent(PointerPressedEvent, args);

		//	//// 4. Process gestures
		//	//// Note: We process the DownEvent *after* the Raise(Pressed), so in case of DoubleTapped
		//	////		 the event is fired after
		//	//if (_gestures.IsValueCreated)
		//	//{
		//	//	// We need to process only events that are bubbling natively to this control,
		//	//	// if they are bubbling in managed it means that they were handled by a child control,
		//	//	// so we should not use them for gesture recognition.
		//	//	_gestures.Value.ProcessDownEvent(args.GetCurrentPoint(this));
		//	//}

		//	//return handledInManaged;
		//}

		///// <summary>
		///// This should be invoked by the native part of the UIElement when a native pointer moved is received
		///// </summary>
		//private bool RaiseNativelyBubbledMove(PointerRoutedEventArgs args, bool isOver)
		//{
		//	//var handledInManaged = false;

		//	////// 1. Update the state
		//	////var wasOver = IsPointerOver;
		//	////IsPointerOver = isOver;

		//	////// 2. Raise enter/exited if needed
		//	////// Note: Entered / Exited are raised *before* the Move (Checked using the args timestamp)
		//	////handledInManaged = RaiseEnteredOrExited(args, wasOver, isOver);
		//	//handledInManaged = SetOver(args, isOver);

		//	//// 3. Raise the Moved event
		//	//var isLocal = isOver || IsCaptured(args.Pointer);
		//	//if (isLocal)
		//	//{
		//	//	args.Handled = false;
		//	//	handledInManaged |= RaiseEvent(PointerMovedEvent, args);
		//	//}

		//	//// 4. Process gestures
		//	//if (isLocal && _gestures.IsValueCreated)
		//	//{
		//	//	// We need to process only events that are bubbling natively to this control,
		//	//	// if they are bubbling in managed it means that they were handled by a child control,
		//	//	// so we should not use them for gesture recognition.
		//	//	_gestures.Value.ProcessMoveEvents(args.GetIntermediatePoints(this));
		//	//}

		//	//return handledInManaged;
		//}

		///// <summary>
		///// This should be invoked by the native part of the UIElement when a native pointer up is received
		///// </summary>
		//private bool RaiseNativelyBubbledUp(PointerRoutedEventArgs args, bool isOver = false)
		//{
		//	//var handledInManaged = false;

		//	//// 1. Update the state
		//	////var wasOver = IsPointerOver;
		//	////IsPointerOver = isOver;
		//	//IsPointerPressed = false; // we do not support multiple pointers at once

		//	//// 2. => For Release step 2. is moved at 5.

		//	//// 3. Raise the Released event
		//	//var isLocal = IsOver(args.Pointer) || IsCaptured(args.Pointer);
		//	//if (isLocal)
		//	//{
		//	//	args.Handled = false; // reset event
		//	//	handledInManaged = RaiseEvent(PointerReleasedEvent, args);
		//	//}

		//	//// 4. Process gestures
		//	//// Note: We process the UpEvent between Release and Exited as the gestures like "Tap"
		//	////		 are fired between those events.
		//	//if (isLocal && _gestures.IsValueCreated)
		//	//{
		//	//	// We need to process only events that are bubbling natively to this control,
		//	//	// if they are bubbling in managed it means that they where handled a child control,
		//	//	// so we should not use them for gesture recognition.
		//	//	_gestures.Value.ProcessUpEvent(args.GetCurrentPoint(this));
		//	//}

		//	////// 5. Raise exited if needed
		//	////// Note: Exited is raise *after* the Released
		//	//////handledInManaged |= RaiseEnteredOrExited(args, wasOver, isOver);
		//	////handledInManaged |= SetOver(args, isOver);

		//	////// 6. Release remaining pointer captures
		//	////// Note: CaptureLost is raise *after* Exited
		//	////handledInManaged |= ReleaseCaptures(args);

		//	//return handledInManaged;
		//}

		private bool ReleaseCaptures(PointerRoutedEventArgs args, bool forceCaptureLostEvent = false)
		{
			if (_pointCaptures.Count > 0)
			{
				ReleasePointerCaptures();
				args.Handled = false;
				return RaiseEvent(PointerCaptureLostEvent, args);
			}
			else if (forceCaptureLostEvent)
			{
				return RaiseEvent(PointerCaptureLostEvent, args);
			}
			else
			{
				return false;
			}
		}

		internal bool IsOver(Pointer pointer) => IsPointerOver;
		internal bool IsPressed(Pointer pointer) => IsPointerPressed;

		///// <summary>
		///// This occurs when the pointer is lost (e.g. when captured by a native control like the ScrollViewer)
		///// which prevents us to continue the touches handling.
		///// </summary>
		//private bool RaiseNativelyBubbledLost(PointerRoutedEventArgs args, bool isOver = false)
		//{
		//	//// When a pointer is captured, we don't even receive "Released" nor "Exited"

		//	//var handledInManaged = false;

		//	//// 1. Update the state
		//	//IsPointerOver = isOver;
		//	//IsPointerPressed = false; // we do not support multiple pointers at once

		//	//// 2. => Exited not raised for PointerLost

		//	//// 3. => Cf. Note on point 6.
		//	//var isLocal = isOver || IsCaptured(args.Pointer);

		//	//// 4. Process gestures
		//	//if (isLocal && _gestures.IsValueCreated)
		//	//{
		//	//	_gestures.Value.CompleteGesture();
		//	//}

		//	//// 5. => Exited not raised for PointerLost

		//	//// 6. Release remaining pointer captures
		//	//// If the pointer was natively captured, it means that we lost all managed captures
		//	//// Note: We should have raise either PointerCaptureLost in 3. or PointerCancelled here in 6. depending of the reason which
		//	////		 drives the system to bubble a lost. However we don't have this kind of information on iOS, and it's
		//	////		 usually due to the ScrollView which kicks in. So we always raise the CaptureLost which is the behavior
		//	////		 on UWP when scroll starts (even if no capture are actives at this time).
		//	//// ReleasePointerCaptures(); // Note this should raise the CaptureLost only if pointer was effectively captured TODO
		//	//args.Handled = false;
		//	//handledInManaged = ReleaseCaptures(args) || RaiseEvent(PointerCaptureLostEvent, args);

		//	//return handledInManaged;
		//}

		private bool SetOver(PointerRoutedEventArgs args, bool isOver, bool isPointerCancelled = false)
		{
			var wasOver = IsPointerOver;
			IsPointerOver = isOver;

			if (isPointerCancelled)
			{
				return false;
			}

			//return RaiseEnteredOrExited(args, wasOver, isOver);

			if (wasOver && !isOver) // Exited
			{
				args.Handled = false;
				return RaiseEvent(PointerExitedEvent, args);
			}
			else if (!wasOver && isOver) // Entered
			{
				args.Handled = false;
				return RaiseEvent(PointerEnteredEvent, args);
			}
			else
			{
				return false;
			}
		}

		private bool SetPressed(PointerRoutedEventArgs args, bool isPressed, bool isPointerCancelled = false)
		{
			var wasPressed = IsPointerPressed;
			IsPointerPressed = isPressed;

			if (isPointerCancelled)
			{
				return false;
			}

			if (wasPressed && !isPressed) // Pressed
			{
				args.Handled = false;
				return RaiseEvent(PointerPressedEvent, args);
			}
			else if (!wasPressed && isPressed) // Released
			{
				args.Handled = false;
				return RaiseEvent(PointerReleasedEvent, args);
			}
			else
			{
				return false;
			}

			//return RaiseEnteredOrExited(args, wasOver, isOver);
			//return false;
		}

		//private bool RaiseEnteredOrExited(PointerRoutedEventArgs args, bool wasOver, bool isOver)
		//{
		//	if (wasOver && !isOver) // Exited
		//	{
		//		args.Handled = false;
		//		return RaiseEvent(PointerExitedEvent, args);
		//	}
		//	else if (!wasOver && isOver) // Entered
		//	{
		//		args.Handled = false;
		//		return RaiseEvent(PointerEnteredEvent, args);
		//	}
		//	else
		//	{
		//		return false;
		//	}
		//}
		#endregion
#endif
#else
		private void InitializePointers() { }
#endif

		#region Pointer capture handling
		/*
		 * About pointer capture
		 *
		 * - When a pointer is captured, it will still bubble up, but it will bubble up from the element
		 *   that captured the touch (so the a inner control won't receive it, even if under the pointer !)
		 *   !!! BUT !!! The OriginalSource will still be the inner control!
		 * - Captured are exclusive : first come, first served! (For a given pointer)
		 * - A control can capture a pointer, even if not under the pointer
		 * - The PointersCapture property remains `null` until a pointer is captured
		 */

		internal bool IsCaptured(Pointer pointer) => _pointCaptures.Any();

		public bool CapturePointer(Pointer value)
		{
			if (_pointCaptures.Contains(value))
			{
				this.Log().Error($"{this}: Pointer {value} already captured.");
			}
			else
			{
				_pointCaptures.Add(value);
#if __WASM__
				CapturePointerNative(value);
#endif
			}
			return true;
		}

		public void ReleasePointerCapture(Pointer value)
		{
			if (_pointCaptures.Contains(value))
			{
				_pointCaptures.Remove(value);
#if __WASM__ || __IOS__
				ReleasePointerCaptureNative(value);
#endif
			}
			else
			{
				this.Log().Error($"{this}: Cannot release pointer {value}: not captured by this control.");
			}
		}

		public void ReleasePointerCaptures()
		{
			if (_pointCaptures.Count == 0)
			{
				this.Log().Warn($"{this}: no pointers to release.");
				return;
			}
#if __WASM__ || __IOS__
			foreach (var pointer in _pointCaptures)
			{
				ReleasePointerCaptureNative(pointer);
			}
#endif
			_pointCaptures.Clear();
		}
		#endregion
	}
}
