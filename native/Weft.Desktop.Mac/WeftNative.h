#pragma once
#include <stdint.h>

// ABI 4: uint32 little-endian JSON length, UTF-8 metadata, then raw textures in block order.
// Textures are shared between placements. Cells use compact arrays. Calls copy all input before returning.
// Handles belong to one window. Zero is invalid. Close is idempotent.
// UTF-8 data has an explicit byte length and may contain NUL.
// A non-null poll buffer belongs to the caller and must be freed once.
// Keep the Native AOT library loaded until process exit.
int32_t weft_abi_version(void);
int64_t weft_open(const void *executable, int32_t length, int32_t width, int32_t height);
int32_t weft_send(int64_t handle, const void *data, int32_t length);
// Notifications run on a worker thread and must only schedule a later poll.
// Clearing the callback or closing the handle drains callbacks before returning.
int32_t weft_notify(int64_t handle, void (*callback)(void *), void *context);
void *weft_poll(int64_t handle, int32_t *length);
void weft_free(void *buffer);
void weft_close(int64_t handle);
