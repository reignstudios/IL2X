#pragma once
#include <stdlib.h>

void IL2X_GC_Init()
{
	// TODO
}

void IL2X_GC_Collect()
{
	// TODO
}

void* IL2X_GC_New(size_t size)
{
	// TODO
	return malloc(size);
}

void* IL2X_GC_NewAtomic(size_t size)
{
	// TODO
	return malloc(size);
}

void* IL2X_GC_Resize(void* object, size_t size)
{
	// TODO
	return realloc(object, size);
}

void IL2X_GC_Delete(void* object)
{
	// TODO
	free(object);
}