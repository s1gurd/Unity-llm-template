using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace CherryFramework.DataModels
{
    [Serializable]
    public abstract class DataModelBase
    {
        private readonly Dictionary<string, List<DownwardBindingHandler>> _handlers = new ();
        private bool _debugMode;
        private bool _bindingsOff;
        private bool _ready;
        private static readonly ConcurrentDictionary<(Type, Type), FillFromCache> _fillFromCache = new();
        
        [JsonIgnore] public string Id { get;  set; } = "";
        [JsonIgnore] public string SlotId { get; set; } = "";

        
        
        protected Dictionary<string, Delegate> Getters = new ();
        protected Dictionary<string, Delegate> Setters = new ();
        
        
        
        [JsonIgnore]
        public bool Ready
        {
            get => _ready;
            set
            {
                if (_ready && value)
                    Debug.LogError($"[{GetType().Name}] Tried to set Ready for model which is already ready!");
                _ready = value; 
                Send(nameof(Ready), value);
            }
        }

        [JsonIgnore]
        public Accessor<bool> ReadyAccessor;

        protected DataModelBase()
        {
            Getters.Add(nameof(Ready), new Func<bool>(() => Ready));
            Setters.Add(nameof(Ready), new Action<bool>(o => Ready = o));
            ReadyAccessor = new Accessor<bool>(this, nameof(Ready));
        }

        public void AddBinding<T>(string memberName, DownwardBindingHandler handler, bool invokeImmediate)
        {
            if (!_handlers.ContainsKey(memberName))
            {
                _handlers.Add(memberName, new List<DownwardBindingHandler> { handler });
            } 
            else
            {
                _handlers[memberName].Add(handler);
            }
            
            if (invokeImmediate && Getters.TryGetValue(memberName, out var getter) && getter != null)
            {
                ((DownwardBindingHandler<T>)handler).DownwardCallback.Invoke(((Func<T>)getter).Invoke());
            }
        }

        public void RemoveBinding(DownwardBindingHandler handler)
        {
            var keys = _handlers.Where(kvp => kvp.Value != null && kvp.Value.Contains(handler)).Select(kvp => kvp.Key).ToList();
            
            for (var i=0; i < keys.Count; i++)
            {
                var handlersList = _handlers[keys[i]].Where(h => !ReferenceEquals(handler, h)).ToList();
                if (handlersList.Count == 0)
                {
                    _handlers.Remove(keys[i]);
                    continue;
                }
                
                _handlers[keys[i]] = handlersList;
            }
        }

        public T GetValue<T>(string memberName)
        {
            if (Getters.TryGetValue(memberName, out var getter))
            {
                return ((Func<T>)getter).Invoke();
            }
            else
            {
                Debug.LogError($"[{GetType().Name}] Not found member with name {memberName} while trying to invoke its downward bindings!");
                return default;
            }
        }

        public void SetValue<T>(string memberName, T value)
        {
            if (Setters.TryGetValue(memberName, out var action) && action != null)
            {
                if (_debugMode)
                {
                    Debug.Log($"[{GetType().Name}] Received upwards {memberName} = {value?.ToString()}");
                }
                (action as Action<T>)!.Invoke(value);
            }
            else
            {
                Debug.LogError($"[{GetType().Name}] Not found member with name {memberName} while trying to set its value");
            }
        }

        public void InvokeBinding<T>(string memberName)
        {
            if (Getters.TryGetValue(memberName, out var getter))
            {
                var value = ((Func<T>)getter).Invoke();
                Send(memberName, value);
            }
            else
            {
                Debug.LogError($"[{GetType().Name}] Not found member with name {memberName} while trying to invoke its downward bindings!");
            }
        }

        public void PauseBindings(bool pause)
        {
            _bindingsOff = pause;
        }

        public void SetDebugMode(bool value)
        {
            _debugMode = value;
        }
        
        public void FillFrom(object instance)
        {
            var cache = _fillFromCache.GetOrAdd((instance.GetType(), GetType()), BuildFillFromCache);
            
            foreach (var thisProp in cache.ModelProps)
            {
                if (cache.FromProps.TryGetValue(thisProp.Name, out var fromProp) && thisProp.PropertyType == fromProp.PropertyType)
                    thisProp.SetValue(this, fromProp.GetValue(instance));
                
                if (cache.FromFields.TryGetValue(thisProp.Name, out var fromField) && thisProp.PropertyType == fromField.FieldType)
                    thisProp.SetValue(this, fromField.GetValue(instance));
            }
        }
        
        private static FillFromCache BuildFillFromCache((Type Source, Type Model) types)
        {
            const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            
            var fromProps = types.Source.GetProperties(bindingFlags).Where(p => p.CanWrite).ToDictionary(x => x.Name);
            var fromFields = types.Source.GetFields(bindingFlags).ToDictionary(x => x.Name);
            var modelProps = types.Model.GetProperties(bindingFlags).Where(p => p.CanWrite).ToList();
            
            return new FillFromCache(modelProps, fromProps, fromFields);
        }
        
        private class FillFromCache
        {
            public readonly List<PropertyInfo> ModelProps;
            public readonly Dictionary<string, PropertyInfo> FromProps;
            public readonly Dictionary<string, FieldInfo> FromFields;
            
            public FillFromCache(List<PropertyInfo> modelProps, Dictionary<string, PropertyInfo> fromProps, Dictionary<string, FieldInfo> fromFields)
            {
                ModelProps = modelProps;
                FromProps = fromProps;
                FromFields = fromFields;
            }
        }
        
        protected void Send<T>(string memberName, T value)
        {
            if (_bindingsOff) return;

            if (_debugMode)
            {
                Debug.Log($"[{GetType().Name}] Send downwards {memberName} = {value?.ToString()}");
            }
            if (!_handlers.TryGetValue(memberName, out var handlers)) return;

            foreach (var handler in handlers)
            {
                (handler as DownwardBindingHandler<T>)!.DownwardCallback.Invoke(value);
            }
        }
    }
}