#pragma once
#include "..\3rdParty\Boehm.GC\include\gc.h"

void IL2X_GC_Init()
{
	GC_INIT();
}

void IL2X_GC_Collect()
{
	GC_gcollect();
}

void* IL2X_GC_New(size_t size)
{
	void* ptr = GC_malloc(size);
	//memset(ptr, 0, size);// GC_malloc will null memory
	return ptr;
}

void* IL2X_GC_NewAtomic(size_t size)
{
	void* ptr = GC_malloc_atomic(size);
	//memset(ptr, 0, size);// GC_malloc will null memory
	return ptr;
}

void* IL2X_GC_NewArray(size_t elementSize, size_t length)
{
	void* result = IL2X_GC_New(sizeof(size_t) + (elementSize * length));
	*((size_t*)result) = length;
	return result;
}

void* IL2X_GC_NewArrayAtomic(size_t elementSize, size_t length)
{
	void* result = IL2X_GC_NewAtomic(sizeof(size_t) + (elementSize * length));
	*((size_t*)result) = length;
	return result;
}

void* IL2X_GC_Resize(void* object, size_t oldSize, size_t newSize)
{
	__int8* ptr = GC_realloc(object, newSize);
	size_t sizeDiff = newSize - oldSize;
	if (sizeDiff > 0) memset(ptr + oldSize, 0, sizeDiff);
	return ptr;
}

void IL2X_GC_Delete(void* object)
{
	GC_free(object);
}

void IL2X_GC_DisableAutoCollections()
{
	/* boehm doesn't support this (do nothing...) */
}

void IL2X_GC_EnableAutoCollections()
{
	/* boehm doesn't support this (do nothing...) */
}

/* ====================================== */
/* manual allocation methods(non-GC heap) */
/* ====================================== */
void* IL2X_Malloc(size_t size)
{
	return malloc(size);
}

void IL2X_Delete(void* ptr)
{
	return free(ptr);
}