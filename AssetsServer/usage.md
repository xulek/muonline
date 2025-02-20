# MuZipper

## Overview
MuZipper is a command-line tool for compressing files into multiple ZIP archives, ensuring each archive does not exceed a specified size limit.

## Features
- Splits files into multiple ZIP archives with a configurable maximum size.
- Generates metadata JSON files for tracking files inside the ZIPs.
- Provides progress updates during the compression process.
- Cleans up existing ZIP files before creating new ones.


## Usage
Double-click the `MuZipper.exe` file to run the application.

By default, the application looks for files in the `./Data` folder and stores the ZIP files in the `./Assets` folder. You can modify these paths in the source code.

## Output
- **ZIP Files**: Created in the `./Assets` directory with names like `data0.zip`, `data1.zip`, etc.
- **Metadata Files**:
  - `./Assets/files.json`: Contains details of all individual files inside the ZIP archives.
  - `index.json`: Contains details of the created ZIP files.

## Serve as a Web Server
You can serve the ZIP files as a web server by running the `http-server` command in the terminal. This will start a local web server on port 8080.

```bash
http-server
```

**Note**: The `http-server` command is not included in the project by default. You can install it using `npm install http-server -g`.
or you can use any other web server of your choice.