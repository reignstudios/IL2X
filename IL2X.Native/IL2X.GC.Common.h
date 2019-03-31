#pragma once
#include <stdlib.h>

#ifdef _WIN32
#include <malloc.h>
#else
#include <alloca.h>
#include <string.h>
#include <wchar.h>
#endif

/* ====================================== */
/* manual allocation methods (non-GC heap) */
/* ====================================== */
void* IL2X_Malloc(size_t size)
{
	void* ptr = malloc(size);
	memset(ptr, 0, size);
	return ptr;
}

void IL2X_Delete(void* ptr)
{
	return free(ptr);
}