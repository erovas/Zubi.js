# Zubi.js
Zubi.js is a lightweight framework for building desktop apps with standard HTML, CSS, and JavaScript, leveraging the user's existing browser. Native functionality is exposed via a C# backend with a plugin system.

## Table of Contents

- [Features](#-features)
- [Architecture](#-architecture)
- [Getting Started](#-getting-started)
  - [Prerequisites](#prerequisites)
  - [Installation](#installation)
  - [Quick Start](#quick-start)
- [Usage](#-usage)
  - [The JavaScript Bridge](#the-javascript-bridge)
  - [Calling Native Methods](#calling-native-methods)
  - [Handling Errors](#handling-errors)
  - [Streaming Data](#streaming-data)
  - [Add Event Listener](#add-event-listener)
- [Plugin Development](#-plugin-development)
  - [Creating a Plugin](#creating-a-plugin)
  - [Plugin Lifecycle](#plugin-lifecycle)
- [Configuration](#-configuration)
- [Contributing](#-contributing)
- [License](#-license)

## Features

- **Use Your Own Browser**: No need to bundle a heavy web view. Zubi.js opens your app in a Chromium-based browser (Chrome, Edge, Brave, Vivaldi) in app mode.
- **Native Power with C#**: Access the file system, system APIs, and any .NET library directly from JavaScript.
- **Plugin Architecture**: Extend functionality with isolated, hot-loadable plugins. Each plugin runs in its own `AssemblyContext`.
- **Secure by Design**: Token-based session management ensures only authorized tabs can communicate with the native backend.
- **Streaming Support**: Transfer large files or data streams efficiently with built-in chunked responses.
- **Event System**: Subscribe to native events from JavaScript seamlessly.
- **Zero Dependencies (Client-Side)**: The client bridge is a tiny, dependency-free JavaScript file.

## Architecture

Zubi.js consists of two main components:

1.  **C# Backend (Host)**: A lightweight HTTP/WebSocket server built on `HttpListener`. It serves your static files and handles RPC calls over WebSockets. It also manages plugins and their lifecycles.
2.  **JavaScript Bridge (Client)**: Injected into your `index.html`. It establishes a WebSocket connection, manages authentication, and provides a `window.Zubi` object to call native methods.

## Getting Started

### Prerequisites

- A modern browser (Chromium-based recommended for app mode)

### Installation

### Quick Start

## Usage

### The JavaScript Bridge

The host injects a script into your index.html. This script creates a global window.Zubi object. You can call any native plugin method as if it were a JavaScript object.

### Calling Native Methods

### Handling Errors

Errors returned from C# are mapped to standard codes. The error object in the catch block has a code property.

### Streaming Data

For large data, you can provide an onChunk callback. The bridge will call it for each chunk of data received.

### Add Event Listener

You can subscribe to C# events exposed by plugins.

## Plugin Development

### Creating a Plugin

1.  Create a new .NET class library project.
2.  Reference the Zubi.js assembly.
3.  Create a class that inherits from Zubi.js.Plugin.
4.  Implement the Dispose(bool) method

### Plugin Lifecycle

- Load: Plugins.Load(assemblyPath) loads the assembly and creates a PluginLoader.
- Instantiate: Plugins.NewInstance(name, args) creates an instance.
- Invoke: Methods are called via reflection.
- Unload: Plugins.Unload(assemblyName) unloads the assembly. It fails if any instances are not disposed.

## Configuration

Zubi.js uses a config.json file in the application's base directory. All fields are optional.

## Contributing

## License

This project is licensed under the MIT License. See the LICENSE file for details.
