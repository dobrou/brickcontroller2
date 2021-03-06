﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BrickController2.Helpers;

namespace BrickController2.PlatformServices.GameController
{  
    public class HttpControllerService : IHttpControllerService, IDisposable
    {
        private IDisposable _listenerSubscription;
        private HttpListener _httpListener;

        public event EventHandler<GameControllerEventArgs> GameControllerEvent;

        public HttpControllerService()
        {
            Start();
        }

        public void Start()
        {

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://*:8080/");
            _httpListener.Start();

            _listenerSubscription =
                // Listen on http endpoint
                Observable.FromAsync(() => _httpListener.GetContextAsync())
                .Repeat()
                // Fire key events from request
                .ObserveOnUsingNewEventLoopSchedulerOnBackground()                
                .Select(ProcessHttpRequest)
                // Send response to client
                .ObserveOn(TaskPoolScheduler.Default)
                .Do(SendResponse)
                .Retry()
                .Subscribe()
            ;
        }

        public void Stop()
        {
            using (_listenerSubscription) _listenerSubscription = null;
            _httpListener.Stop();
            _lastSeenKeyOrder.Clear();
        }

        public void Dispose()
        {
            Stop();
        }

        private readonly Dictionary<(GameControllerEventType type, string key), long> _lastSeenKeyOrder = new Dictionary<(GameControllerEventType type, string key), long>();

        (HttpListenerContext request, HttpStatusCode code, string message) ProcessHttpRequest(HttpListenerContext request)
        {
            try
            {
                // Example: http://phoneip:8080/Button/X/1
                // Example: http://phoneip:8080/Axis/X/-0.5                
                var tokens = request?.Request?.Url?.AbsolutePath?.Trim('/').Split('/') ?? new string[0];

                var tokensPerEvent = 4;

                if (tokens.Length < tokensPerEvent)
                {
                    // unknown url pattern, show main page
                    return (request, HttpStatusCode.OK, defaultWebPage);
                }

                var type = Enum.TryParse<GameControllerEventType>(tokens[0], out var _type) ? _type : GameControllerEventType.Button;
                var key = tokens[1];
                var value = float.TryParse(tokens[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var _value) ? _value : 0;
                var order = long.TryParse(tokens[3], out var _order) ? _order : long.MinValue;
                // make sure to skip out of order key presses, so that we won't override eg. last button release event
                var isSkipped =
                    order != long.MinValue
                    && _lastSeenKeyOrder.TryGetValue((type, key), out var lastOrder)
                    && order < lastOrder
                ;
            
                // write current order to cache
                if(!isSkipped && order != long.MinValue)
                    _lastSeenKeyOrder[(type, key)] = order;


                if (!isSkipped)
                    GameControllerEvent?.Invoke(this, new GameControllerEventArgs(type, key, value));

                return (request, HttpStatusCode.OK, $"{type}:{key}:{value}:{isSkipped}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return (request, HttpStatusCode.InternalServerError, ex.ToString());
            }
        }

        void SendResponse((HttpListenerContext request, HttpStatusCode code, string message) request)
        {
            try
            {
                request.request.Response.StatusCode = (int) request.code;
                request.request.Response.StatusDescription = request.code.ToString();
                request.request.Response.ContentType = "text/html; charset=UTF-8";
                request.request.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                var buffer = Encoding.UTF8.GetBytes(request.message);
                request.request.Response.Close(buffer, false);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private string defaultWebPage = @"
<html>
<head>
<title>Brick Controller</title>
<style>
div#controls input[type=range] {
  width: 1pc;
  height: 10pc;
  padding: 2pc;
  margin: 1pc 0;
  -webkit-appearance: slider-vertical;  
}
div.joy {  
  width: 49%;
  height: 15pc;
  margin: 0.5%;
  position: relative;
  float:left;
  filter: brightness(85%);
}
</style>

<script type='text/javascript'>
  var isGyroEnabled = false;
  var gyroBase = null;

  if (window.DeviceOrientationEvent) {
    window.addEventListener('deviceorientation', function () {
      processGyro({a: event.alpha, b: event.beta, g: event.gamma});
    }, true);
  } 

  function processGyro(gyro) {
    if(!isGyroEnabled) return;
    
    // Android
    // alpha 0..360
    // beta -180..180 => 0..360
    // gamma -90..0..90|-90..0..-90 => 0..180|0..180
    var gyroNorm = {
      a: gyro.a,
      b: gyro.b + 180,
      g: gyro.g + 90,
    };

    if(!gyroBase) gyroBase = gyroNorm;
    
    var gyroRange = document.getElementById('gyrorangedeg').value;

    var gyroDelta = {
      a: circleSmallestAngleDistance(gyroNorm.a, gyroBase.a) / gyroRange,
      b: circleSmallestAngleDistance(gyroNorm.b, gyroBase.b) / gyroRange,
      g: circleSmallestAngleDistance(gyroNorm.g*2, gyroBase.g*2)/2 / gyroRange,
    };
    
    //sendEventThrottled('Axis', 'gyroA', gyroDelta.a);
    sendEventThrottled('Axis', 'gyroB', gyroDelta.b);
    sendEventThrottled('Axis', 'gyroG', gyroDelta.g);
    
    document.getElementById('gyroinfo').innerHTML = 
      roundValue(gyroDelta.a) +' '+ roundValue(gyroDelta.b) +' '+ roundValue(gyroDelta.g)      
      +'<br>'+ 
      roundValue(gyroNorm.a,0) +' '+ roundValue(gyroNorm.b,0) +' '+ roundValue(gyroNorm.g,0)
      +'<br>'+ 
      roundValue(gyro.a,0) +' '+ roundValue(gyro.b,0) +' '+ roundValue(gyro.g,0)      
    ;        
  }  
  
  function circleSmallestAngleDistance(a, b) {
    return ((a - b + 180 + 360) % 360 - 180);
  }
    
  function togglegyro() {
    isGyroEnabled = !isGyroEnabled;
    
    gyroBase = null;
    
    sendEventThrottled('Axis', 'gyroA', 0);
    sendEventThrottled('Axis', 'gyroB', 0);
    sendEventThrottled('Axis', 'gyroG', 0);
    
    document.getElementById('togglegyro').value = isGyroEnabled ? 'Stop Gyro' : 'Start Gyro' ;
  }
</script>

<script type='text/javascript'>
  document.addEventListener('keydown', function(event) {
    if(event.repeat) return;  
    sendButtonEvent(event.key, 1);
  });
  document.addEventListener('keyup', function(event) {
    sendButtonEvent(event.key, 0);
  });

  function sendButtonEvent(key, value) {
    if(key.length == 1) key = key.toUpperCase();
    sendEvent('Button', key, value);
  }
    
  function sendEvent(type, key, value) {   
    value = roundValue(value); 
    var command = '/'+type+'/'+key+'/'+value+'/'+(new Date().getTime());
    var url = 'http://' + document.getElementById('server').value + command;
  
    document.getElementById('lastkey').textContent = command;
    console.log(url);

    var Http = new XMLHttpRequest();
    Http.open('GET', url);
    Http.send();
    Http.onload = (e)=>{ 
      document.getElementById('lastresponse').textContent = Http.responseText;
      console.log(Http.responseText) 
    };
  }

  function roundValue(value, decimals) {
    var f = Math.pow(10, decimals === undefined ? 2 : decimals);
    return Math.round(value*f)/f;
  }

  function resetJoy(range) {
    if( document.getElementById('joyautoreset').checked ) 
      range.value = 0;
      
    sendJoyEvent(range);
  }
  
  function sendJoyEvent(range) {
    sendEventThrottled('Axis', range.id, range.value );
  }
    
  function sendEventThrottled(type, key, value) { 
    var time = value == 0 ? 0 : 50;
    throttle( type+'.'+key, time, _ => sendEvent(type, key, value) );
  }

  var throttleNextRun = {};
  function throttle(id, time, func) {
    if(time == 0){
      // no scheduling and no throttling when time is zero
      func();
      if(throttleNextRun[id]) throttleNextRun[id] = null;
    } else if(throttleNextRun[id] === undefined){
      // undefined -> no event since now-time, execute immediately and block throttle window with null
      func();
      throttleNextRun[id] = null;
      setTimeout(function() { 
        var f = throttleNextRun[id]; 
        throttleNextRun[id] = undefined; 
        if(f) throttle(id, time, f);
      }, time);
    }else{
      throttleNextRun[id] = func;
    }
  };
</script>

</head>
<body>
<div>
  Press any key to send it as button event.
</div>
<div>
  Touch and drag color or sliders boxes as joystick.
</div>
<div>
  Last key pressed: <div id='lastkey' ></div>
</div>
<div>
  Last key response: <div id='lastresponse' ></div>
</div>
<div>
  <input type='button' id='togglegyro' onclick='togglegyro();' value='Start Gyro'></input>
  <label for='gyrorangedeg'>Max range degrees</label>
  <input type='text' size='3' id='gyrorangedeg' value='35'></input>
  <div id='gyroinfo'></div>
</div>
<div id='joycontainer'>
</div>
<div id='controls'>
  <input type='range' min='-1' max='1' step='0.1' id='X' onInput='sendJoyEvent(this);' onTouchEnd='resetJoy(this);' onMouseUp='resetJoy(this);' ></input>
  <input type='range' min='-1' max='1' step='0.1' id='Y' onInput='sendJoyEvent(this);' onTouchEnd='resetJoy(this);' onMouseUp='resetJoy(this);' ></input>
  <input type='range' min='-1' max='1' step='0.1' id='A' onInput='sendJoyEvent(this);' onTouchEnd='resetJoy(this);' onMouseUp='resetJoy(this);' ></input>
  <input type='range' min='-1' max='1' step='0.1' id='B' onInput='sendJoyEvent(this);' onTouchEnd='resetJoy(this);' onMouseUp='resetJoy(this);' ></input>
  <input type='range' min='-1' max='1' step='0.1' id='C' onInput='sendJoyEvent(this);' onTouchEnd='resetJoy(this);' onMouseUp='resetJoy(this);' ></input>
  <input type='range' min='-1' max='1' step='0.1' id='D' onInput='sendJoyEvent(this);' onTouchEnd='resetJoy(this);' onMouseUp='resetJoy(this);' ></input>
</div>
<div>
  <input type='checkbox' id='joyautoreset' checked  ></input>
  <label for='joyautoreset'>Joystick position auto reset</label>
</div>
<div>
  <input type='text' id='server' value=''></input>
  <label for='server'>Brick Controller address</label>
  <script type='text/javascript'>document.getElementById('server').value = window.location.host</script>
</div>

<script type='text/javascript' src='https://yoannmoinet.github.io/nipplejs/javascripts/nipplejs.js'></script>
<script type='text/javascript'>
  function createJoy(id, color, xname, yname){
    var container = document.getElementById('joycontainer');
    var joyElement = document.createElement('div');
    joyElement.id = 'joy'+id;
    joyElement.className = 'joy';
    joyElement.style.backgroundColor = 'hsl('+color+',90%,80%)';
    container.appendChild(joyElement);
    
    var joyRadius = 100;
    var options = {
        zone: document.getElementById('joy'+id),
        fadeTime: 250,
        size: joyRadius*2,
        color: 'hsl('+color+',80%,50%)',
    };
    var manager = nipplejs.create(options);
    manager.on('move', function (evt, data) {
      var position = manager.get(data.identifier).frontPosition;
      var dx =  position.x / joyRadius;
      var dy = -position.y / joyRadius;
      
      sendEventThrottled('Axis', xname, dx);
      sendEventThrottled('Axis', yname, dy);    
    });  
    manager.on('end', function (evt, data) {
      sendEventThrottled('Axis', xname, 0);
      sendEventThrottled('Axis', yname, 0);    
    });  
  }
  
  createJoy(1, 10,  'X', 'Y');
  createJoy(2, 320, 'A', 'B');
  createJoy(3, 140, 'C', 'D');
  createJoy(4, 60,  'E', 'F');
  createJoy(5, 220, '1', '2');
  createJoy(6, 180, '3', '4');
</script>

</body>
</html>
";

    }
}
