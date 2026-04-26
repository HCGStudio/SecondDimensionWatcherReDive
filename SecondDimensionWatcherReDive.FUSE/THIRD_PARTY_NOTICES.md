# Third-Party Notices for `sdwfuse`

`sdwfuse` (the FUSE client in `SecondDimensionWatcherReDive.FUSE`) is licensed
under the Apache License 2.0 (see `LICENSE`). It depends on the third-party
components listed below at runtime. This file exists to satisfy the notice
requirements those licenses impose on downstream distributors.

---

## libfuse3

- **Project:** libfuse — Filesystem in Userspace, version 3
- **Upstream:** https://github.com/libfuse/libfuse
- **License:** GNU Lesser General Public License, version 2.1 (LGPL-2.1-only)
- **License text:** https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html
- **Linkage:** dynamic. `sdwfuse` calls `fuse_main_real` from
  `libfuse3.so.3`, which the user installs through their distribution
  package manager (`apt install libfuse3-3`, `dnf install fuse3-libs`,
  `pacman -S fuse3`, etc.). No part of libfuse3 is bundled in the
  `sdwfuse` binary, statically linked into it, or redistributed by this
  project in source or object form.

The Apache-2.0 license under which `sdwfuse` is distributed permits an end
user to run this program against any version of libfuse3 they choose to
install on their system, including a modified version of libfuse3 they have
built themselves. Per LGPL-2.1 §6 this is the configuration we rely on for
license compatibility.

If you redistribute the `sdwfuse` binary together with libfuse3 (e.g. by
bundling it inside a container image), you assume the LGPL-2.1
re-distribution obligations for libfuse3 — namely, you must either
distribute the corresponding source of the libfuse3 build you shipped or
include a written offer to do so on request, and the user must remain able
to relink the program against a modified libfuse3.

The official `sdwfuse` `.deb` / `.rpm` / `pkg.tar.zst` packages do **not**
bundle libfuse3; they declare a runtime dependency on the distro's libfuse3
package, which is the cleanest path to compliance.

---

## .NET Runtime (NativeAOT)

- **Project:** .NET 10 — `Microsoft.NETCore.App.Runtime.NativeAOT.linux-*`
- **Upstream:** https://github.com/dotnet/runtime
- **License:** MIT
- **Linkage:** static. The NativeAOT toolchain links the .NET runtime
  archives (`libSystem.Native.a`, `libRuntime.WorkstationGC.a`, etc.) into
  the produced `sdwfuse` binary. The .NET runtime's MIT license requires
  preservation of the copyright notice and license text when redistributing
  in binary form; both are reproduced in the upstream repository linked
  above and are included in `sdwfuse` distributions in this notice.

A copy of the MIT license, as applied to the .NET runtime, is reproduced
below.

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
