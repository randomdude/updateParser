#include "pch.h"
#include <vcclr.h>

#include "wimprocessing.h"

#include "..\thirdparty\\wimlib.h"

using namespace System;
using namespace System::Runtime::InteropServices;

wimlib_iterate_dir_tree_callback_t
int wimlib_iterate_lookup_table_cb(const struct wimlib_resource_entry *resource, void *user_ctx);

class wimprocessor_native
{
public:
	WIMStruct* wimHandle;
};

ref class wimprocessor
{
public:
	static void wimprocessor::handleWimFile(System::String^ sourcefilename)
	{
		IntPtr sourcefilenamePtr = Marshal::StringToHGlobalUni(sourcefilename);
		wchar_t* sourcefilenameChars = static_cast<wchar_t*>(sourcefilenamePtr.ToPointer());

		wimprocessor_native nativeInfo;

		int s =  wimlib_open_wim(sourcefilenameChars, 0, &nativeInfo.wimHandle);
		if (s != 0)
		{
			Marshal::FreeHGlobal(sourcefilenamePtr);
			throw gcnew Exception("Failed wimlib_open_wim");
		}
		
		wimlib_iterate_lookup_table(nativeInfo.wimHandle, 0, wimlib_iterate_lookup_table_cb, &nativeInfo);

		Marshal::FreeHGlobal(sourcefilenamePtr);
	}

	static int onLookupTableEntry(const struct wimlib_resource_entry *resource, wimprocessor_native* nativeInfo)
	{
		resource.
		return 0;
	}

};

int wimlib_iterate_lookup_table_cb(const struct wimlib_resource_entry *resource, void *user_ctx)
{
	return wimprocessor::onLookupTableEntry(resource, (wimprocessor_native*)user_ctx);
}


